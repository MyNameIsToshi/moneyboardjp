using System.Reflection;
using MoneyBoardShared;

namespace MoneyBoard.Services;

/// <summary>実行中アセンブリの表示用バージョン文字列（"vX.Y.Z"）。AppTitle・AppUpdateService で共有する。</summary>
public static class AppVersionProvider
{
    public static readonly string Current = AppVersionMath.FormatDisplayVersion(
        typeof(AppVersionProvider).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);
}
