using System;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;

/// <summary>同じプロセス内の初期化を共有し、複数ウィンドウの匿名ユーザーを分離する。</summary>
public static class RelayConnectionService
{
    private static readonly string profile = "relay_" + Guid.NewGuid().ToString("N").Substring(0, 24);
    private static Task initialization;

    public static Task EnsureReadyAsync()
    {
        if (initialization == null || initialization.IsFaulted || initialization.IsCanceled
            || (initialization.IsCompleted && !AuthenticationService.Instance.IsSignedIn))
        {
            initialization = InitializeAsync();
        }

        return initialization;
    }

    private static async Task InitializeAsync()
    {
        if (UnityServices.State != ServicesInitializationState.Initialized)
        {
            var options = new InitializationOptions().SetProfile(profile);
            await UnityServices.InitializeAsync(options);
        }

        if (!AuthenticationService.Instance.IsSignedIn)
        {
            AuthenticationService.Instance.SwitchProfile(profile);
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }
    }
}
