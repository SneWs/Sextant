using System.ComponentModel;
using Dock.Model.Mvvm.Controls;
using Sextant.ViewModels;

namespace Sextant.Docking;

public sealed class RepositoryDocument : Document
{
    public RepositoryDocument(RepositoryViewModel repository)
    {
        Repository = repository;
        Id = "document-" + Guid.NewGuid().ToString("N");
        Title = repository.Title;
        CanClose = true;
        CanFloat = true;
        repository.PropertyChanged += OnRepositoryChanged;
    }

    public RepositoryViewModel Repository { get; }

    private void OnRepositoryChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(RepositoryViewModel.Title))
            Title = Repository.Title;
    }
}
