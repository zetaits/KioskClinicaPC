using Microsoft.Management.Deployment;

namespace Kiosk.SetupHelper;

/// <summary>Official CsWinRT activation factory backed by the native DLL shipped in ComInterop.
/// The SDK handles registered/manual out-of-process activation; no private COM interfaces are copied.</summary>
internal static class WinGetActivation
{
    public static T Create<T>() => (T)(object)(typeof(T).Name switch
    {
        nameof(PackageManager) => (object)new PackageManager(),
        nameof(FindPackagesOptions) => new FindPackagesOptions(),
        nameof(PackageMatchFilter) => new PackageMatchFilter(),
        nameof(InstallOptions) => new InstallOptions(),
        nameof(CreateCompositePackageCatalogOptions) => new CreateCompositePackageCatalogOptions(),
        _ => throw new NotSupportedException(typeof(T).Name)
    });
}
