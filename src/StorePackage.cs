using System.Runtime.InteropServices;
using System.Text;

namespace Sextant;

/// <summary>
/// The Microsoft Store package. The zip and a sideload MSIX keep Check for update.
/// The Store delivers its own updates, so that install does not ask GitHub.
/// </summary>
internal static class StorePackage
{
    /// <summary>Partner Center family name. It matches <c>make-msix.ps1</c>.</summary>
    internal const string FamilyName = "20817M.Grenangen.SextantGit_nr8zhaqknvty0";

    const int AppModelErrorNoPackage = 15700;

    /// <summary>Tests force the Store install or the zip install. Null reads the process.</summary>
    internal static bool? Override { get; set; }

    public static bool IsStoreInstall => Override ?? Detect();

    internal static bool IsStoreFamily(string? familyName) =>
        string.Equals(familyName, FamilyName, StringComparison.Ordinal);

    private static bool Detect()
    {
        if (!OperatingSystem.IsWindows())
            return false;
        try
        {
            var length = 0;
            if (GetCurrentPackageFamilyName(ref length, null) == AppModelErrorNoPackage || length <= 1)
                return false;
            var name = new StringBuilder(length);
            if (GetCurrentPackageFamilyName(ref length, name) != 0)
                return false;
            return IsStoreFamily(name.ToString());
        }
        catch (Exception)
        {
            return false;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFamilyName(ref int packageFamilyNameLength, StringBuilder? packageFamilyName);
}
