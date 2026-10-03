using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Sextant.ViewModels;

namespace Sextant.Views;

/// <summary>
/// Virtualizes the diff, and sizes the scroll extent from each row.
/// A picture is much taller than a text line. Averaging the rows on screen
/// leaves the files after those pictures past the end of the scrollbar.
/// </summary>
public sealed class DiffVirtualizingPanel : VirtualizingPanel
{
    private static readonly AttachedProperty<object?> RecycleKeyProperty =
        AvaloniaProperty.RegisterAttached<DiffVirtualizingPanel, Control, object?>("RecycleKey");

    private readonly List<Slot> _realized = [];
    private readonly Dictionary<object, Stack<Control>> _pool = [];
    private double[] _heights = [];
    private double _line = 22;
    private double _block = 44;
    private double _image = 520;
    private int _scrollTo = -1;
    private int _scrollPasses;
    private bool _nudgePosted;
    private Rect _viewport;
    private ScrollViewer? _scroll;

    private readonly record struct Slot(int Index, Control Control);

    public DiffVirtualizingPanel()
    {
        EffectiveViewportChanged += (_, e) =>
        {
            _viewport = e.EffectiveViewport;
            InvalidateMeasure();
        };
    }

    protected override Size MeasureOverride(Size available)
    {
        var items = Items;
        EnsureHeights(items.Count);
        if (items.Count == 0 || ItemContainerGenerator is null)
        {
            RecycleAll();
            return default;
        }

        var width = available.Width;
        if (double.IsInfinity(width) || width < 1)
            width = Bounds.Width > 1 ? Bounds.Width : 800;

        var (top, bottom) = VisibleSpan();
        Realize(items, width, top, bottom);
        if (_scrollTo >= 0 && !InView(_scrollTo))
            RequestScroll(_scrollTo);
        return new Size(double.IsInfinity(available.Width) ? width : available.Width, ExtentHeight(items));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var slot in _realized)
        {
            var height = HeightOf(slot.Index);
            slot.Control.Arrange(new Rect(0, Prefix(slot.Index), finalSize.Width, height));
        }

        return finalSize;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _scroll = this.FindAncestorOfType<ScrollViewer>();
        if (_scroll is not null)
            _scroll.ScrollChanged += OnScrollChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_scroll is not null)
            _scroll.ScrollChanged -= OnScrollChanged;
        _scroll = null;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnItemsControlChanged(ItemsControl? oldValue)
    {
        base.OnItemsControlChanged(oldValue);
        RecycleAll();
        _heights = [];
        _pool.Clear();
    }

    protected override void OnItemsChanged(IReadOnlyList<object?> items, NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                InsertHeights(e.NewStartingIndex, e.NewItems!.Count);
                ShiftRealized(e.NewStartingIndex, e.NewItems.Count);
                break;
            case NotifyCollectionChangedAction.Remove:
                DropRange(e.OldStartingIndex, e.OldItems!.Count);
                RemoveHeights(e.OldStartingIndex, e.OldItems.Count);
                ShiftRealized(e.OldStartingIndex, -e.OldItems.Count);
                break;
            case NotifyCollectionChangedAction.Replace:
                DropRange(e.OldStartingIndex, e.OldItems!.Count);
                for (var i = 0; i < e.OldItems.Count; i++)
                {
                    var index = e.OldStartingIndex + i;
                    if (index >= 0 && index < _heights.Length)
                        _heights[index] = 0;
                }

                break;
            case NotifyCollectionChangedAction.Move:
            case NotifyCollectionChangedAction.Reset:
                RecycleAll();
                _heights = [];
                break;
        }

        InvalidateMeasure();
    }

    protected override Control? ScrollIntoView(int index)
    {
        var items = Items;
        if (index < 0 || index >= items.Count || ItemContainerGenerator is null)
            return null;
        EnsureHeights(items.Count);
        _scrollPasses = 0;
        var width = Bounds.Width > 1 ? Bounds.Width : 800.0;
        var control = FindRealized(index) ?? Create(items, index);
        control.Measure(new Size(width, double.PositiveInfinity));
        Remember(index, items[index], control.DesiredSize.Height);
        var height = Math.Max(HeightOf(index), control.DesiredSize.Height);
        control.Arrange(new Rect(0, Prefix(index), width, height));
        RequestScroll(index);
        InvalidateMeasure();
        return control;
    }

    protected override Control? ContainerFromIndex(int index) => FindRealized(index);

    protected override int IndexFromContainer(Control container)
    {
        foreach (var slot in _realized)
        {
            if (ReferenceEquals(slot.Control, container))
                return slot.Index;
        }

        return -1;
    }

    protected override IEnumerable<Control>? GetRealizedContainers()
    {
        foreach (var slot in _realized)
            yield return slot.Control;
    }

    protected override IInputElement? GetControl(NavigationDirection direction, IInputElement? from, bool wrap)
    {
        var count = Items.Count;
        if (count == 0)
            return null;
        var fromIndex = -1;
        for (var node = from as Visual; node is not null; node = node.GetVisualParent())
        {
            if (node is Control control)
            {
                fromIndex = IndexFromContainer(control);
                if (fromIndex >= 0)
                    break;
            }
        }

        var to = fromIndex;
        switch (direction)
        {
            case NavigationDirection.First:
                to = 0;
                break;
            case NavigationDirection.Last:
                to = count - 1;
                break;
            case NavigationDirection.Next:
            case NavigationDirection.Down:
            case NavigationDirection.Right:
                to++;
                break;
            case NavigationDirection.Previous:
            case NavigationDirection.Up:
            case NavigationDirection.Left:
                to--;
                break;
            default:
                return null;
        }

        if (to == fromIndex)
            return from;
        if (wrap)
        {
            if (to < 0)
                to = count - 1;
            else if (to >= count)
                to = 0;
        }
        else if (to < 0 || to >= count)
        {
            return null;
        }

        return ScrollIntoView(to);
    }

    private void RequestScroll(int index)
    {
        _scrollTo = index;
        if (_nudgePosted || _scrollPasses > 8)
            return;
        _nudgePosted = true;
        _scrollPasses++;
        Dispatcher.UIThread.Post(() =>
        {
            _nudgePosted = false;
            if (_scrollTo != index)
                return;
            NudgeScroll(index);
            if (InView(index))
                _scrollTo = -1;
            else
                InvalidateMeasure();
        }, DispatcherPriority.Loaded);
    }

    private void NudgeScroll(int index)
    {
        if (_scroll is null)
            _scroll = this.FindAncestorOfType<ScrollViewer>();
        if (_scroll is not { Viewport.Height: > 1 } || index < 0 || index >= Items.Count)
            return;
        var y = Prefix(index);
        var height = HeightOf(index);
        var top = _scroll.Offset.Y;
        var bottom = top + _scroll.Viewport.Height;
        if (y >= top - 1 && y + height <= bottom + 1)
            return;
        var target = y < top ? y : y + height - _scroll.Viewport.Height;
        var max = Math.Max(0, ExtentHeight(Items) - _scroll.Viewport.Height);
        target = Math.Clamp(target, 0, max);
        if (Math.Abs(_scroll.Offset.Y - target) > 1)
            _scroll.Offset = new Vector(_scroll.Offset.X, target);
    }

    private bool InView(int index)
    {
        if (_scroll is not { Viewport.Height: > 1 } || index < 0 || index >= Items.Count)
            return false;
        var y = Prefix(index);
        var bottom = y + HeightOf(index);
        var top = _scroll.Offset.Y;
        var viewBottom = top + _scroll.Viewport.Height;
        return bottom > top + 1 && y < viewBottom - 1;
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (Math.Abs(e.OffsetDelta.Y) < 0.5 && Math.Abs(e.ViewportDelta.Y) < 0.5)
            return;
        InvalidateMeasure();
    }

    private (double Top, double Bottom) VisibleSpan()
    {
        if (_scroll is null)
            _scroll = this.FindAncestorOfType<ScrollViewer>();
        if (_scroll is { Viewport.Height: > 1 })
        {
            var top = Math.Max(0, _scroll.Offset.Y - 200);
            return (top, _scroll.Offset.Y + _scroll.Viewport.Height + 200);
        }

        if (_viewport.Height > 1)
            return (Math.Max(0, _viewport.Top - 200), _viewport.Bottom + 200);
        return (0, 800);
    }

    private void Realize(IReadOnlyList<object?> items, double width, double top, double bottom)
    {
        var start = IndexAt(top, items.Count);
        var end = start;
        var y = Prefix(start);
        while (end < items.Count && y < bottom)
        {
            y += HeightOf(end);
            end++;
        }

        if (end == start && items.Count > 0)
            end = Math.Min(items.Count, start + 1);

        for (var i = _realized.Count - 1; i >= 0; i--)
        {
            var slot = _realized[i];
            if (slot.Index >= start && slot.Index < end)
                continue;
            if (HoldsFocus(slot.Control))
                continue;
            Recycle(slot.Control);
            _realized.RemoveAt(i);
        }

        var measure = new Size(width, double.PositiveInfinity);
        for (var index = start; index < end; index++)
        {
            var control = FindRealized(index) ?? Create(items, index);
            control.Measure(measure);
            Remember(index, items[index], control.DesiredSize.Height);
        }
    }

    private Control Create(IReadOnlyList<object?> items, int index)
    {
        var generator = ItemContainerGenerator!;
        var item = items[index];
        Control container;
        if (!generator.NeedsContainer(item, index, out var key))
        {
            container = (Control)item!;
            container.IsVisible = true;
            generator.PrepareItemContainer(container, item, index);
            if (!Children.Contains(container))
                AddInternalChild(container);
            generator.ItemContainerPrepared(container, item, index);
        }
        else
        {
            container = Take(key) ?? generator.CreateContainer(item, index, key);
            if (key is not null)
                container.SetValue(RecycleKeyProperty, key);
            container.IsVisible = true;
            generator.PrepareItemContainer(container, item, index);
            AddInternalChild(container);
            generator.ItemContainerPrepared(container, item, index);
        }

        _realized.Add(new Slot(index, container));
        return container;
    }

    private Control? Take(object? key)
    {
        if (key is null || !_pool.TryGetValue(key, out var pool) || pool.Count == 0)
            return null;
        return pool.Pop();
    }

    private void Recycle(Control element)
    {
        var generator = ItemContainerGenerator;
        var key = element.GetValue(RecycleKeyProperty);
        generator?.ClearItemContainer(element);
        if (key is not null)
        {
            if (!_pool.TryGetValue(key, out var pool))
            {
                pool = new Stack<Control>();
                _pool[key] = pool;
            }

            pool.Push(element);
        }

        element.IsVisible = false;
        if (Children.Contains(element))
            RemoveInternalChild(element);
    }

    private void RecycleAll()
    {
        for (var i = _realized.Count - 1; i >= 0; i--)
            Recycle(_realized[i].Control);
        _realized.Clear();
    }

    private void DropRange(int start, int count)
    {
        if (count <= 0)
            return;
        var end = start + count;
        for (var i = _realized.Count - 1; i >= 0; i--)
        {
            var slot = _realized[i];
            if (slot.Index < start || slot.Index >= end)
                continue;
            Recycle(slot.Control);
            _realized.RemoveAt(i);
        }
    }

    private void ShiftRealized(int start, int delta)
    {
        if (delta == 0)
            return;
        var generator = ItemContainerGenerator;
        for (var i = 0; i < _realized.Count; i++)
        {
            var slot = _realized[i];
            if (slot.Index < start)
                continue;
            var updated = slot.Index + delta;
            generator?.ItemContainerIndexChanged(slot.Control, slot.Index, updated);
            _realized[i] = new Slot(updated, slot.Control);
        }
    }

    private Control? FindRealized(int index)
    {
        foreach (var slot in _realized)
        {
            if (slot.Index == index)
                return slot.Control;
        }

        return null;
    }

    private bool HoldsFocus(Control control)
    {
        if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is not Visual focused)
            return false;
        return ReferenceEquals(focused, control) || control.IsVisualAncestorOf(focused);
    }

    private void EnsureHeights(int count)
    {
        if (_heights.Length == count)
            return;
        var next = new double[count];
        Array.Copy(_heights, next, Math.Min(_heights.Length, count));
        _heights = next;
    }

    private void InsertHeights(int index, int count)
    {
        if (count <= 0)
            return;
        if (index < 0)
            index = 0;
        if (index > _heights.Length)
            index = _heights.Length;
        var next = new double[_heights.Length + count];
        if (index > 0)
            Array.Copy(_heights, 0, next, 0, index);
        var tail = _heights.Length - index;
        if (tail > 0)
            Array.Copy(_heights, index, next, index + count, tail);
        _heights = next;
    }

    private void RemoveHeights(int index, int count)
    {
        if (count <= 0 || _heights.Length == 0)
            return;
        if (index < 0)
            index = 0;
        if (index >= _heights.Length)
            return;
        count = Math.Min(count, _heights.Length - index);
        var next = new double[_heights.Length - count];
        if (index > 0)
            Array.Copy(_heights, 0, next, 0, index);
        var tail = _heights.Length - (index + count);
        if (tail > 0)
            Array.Copy(_heights, index + count, next, index, tail);
        _heights = next;
    }

    private bool Remember(int index, object? item, double measured)
    {
        if (measured < 1 || index < 0 || index >= _heights.Length)
            return false;
        var previous = _heights[index];
        if (previous >= 1 && Math.Abs(previous - measured) < 0.5)
            return false;
        _heights[index] = measured;
        switch (item)
        {
            case DiffImageRow:
                _image = measured;
                break;
            case DiffLineRow or DiffSideRow:
                _line = measured;
                break;
            case DiffFileRow or DiffHunkRow:
                _block = measured;
                break;
        }

        return true;
    }

    private double HeightOf(int index)
    {
        if (index >= 0 && index < _heights.Length && _heights[index] >= 1)
            return _heights[index];
        return Estimate(index);
    }

    private double Estimate(int index)
    {
        if (index < 0 || index >= Items.Count)
            return _line;
        return Items[index] switch
        {
            DiffImageRow => _image,
            DiffLineRow or DiffSideRow => _line,
            _ => _block,
        };
    }

    private double Prefix(int index)
    {
        var sum = 0.0;
        var count = Math.Min(index, Items.Count);
        for (var i = 0; i < count; i++)
            sum += HeightOf(i);
        return sum;
    }

    private double ExtentHeight(IReadOnlyList<object?> items)
    {
        var sum = 0.0;
        for (var i = 0; i < items.Count; i++)
            sum += HeightOf(i);
        return sum;
    }

    private int IndexAt(double y, int count)
    {
        var cursor = 0.0;
        for (var i = 0; i < count; i++)
        {
            var height = HeightOf(i);
            if (cursor + height > y)
                return i;
            cursor += height;
        }

        return Math.Max(0, count - 1);
    }
}
