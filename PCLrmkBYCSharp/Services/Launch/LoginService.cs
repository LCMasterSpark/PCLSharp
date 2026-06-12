using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services.Launch;

public sealed class LoginService(
    ILegacyLoginService legacy,
    IMicrosoftLoginService microsoft,
    IYggdrasilLoginService yggdrasil,
    IAppSettingsService settings,
    IMojangProfileService? mojangProfiles = null) : ILoginService
{
    public async Task<LoginSession> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        settings.Set(AppSettingKeys.LoginType, request.Type);
        settings.Set(AppSettingKeys.LoginRemember, request.Remember);
        switch (request.Type)
        {
            case LoginType.Legacy:
                var skinType = settings.Get(AppSettingKeys.LaunchSkinType, 0);
                var skinName = settings.Get(AppSettingKeys.LaunchSkinID, "");
                var skinUuid = "";
                if (skinType == 3 && !string.IsNullOrWhiteSpace(skinName) && mojangProfiles is not null)
                {
                    skinUuid = await mojangProfiles.GetUuidAsync(skinName, cancellationToken).ConfigureAwait(false) ?? "";
                }

                var session = legacy.CreateSession(
                    request.LegacyName,
                    skinType,
                    settings.Get(AppSettingKeys.LaunchSkinSlim, false),
                    skinName,
                    skinUuid);
                legacy.SaveHistory(session.UserName, settings);
                await settings.SaveAsync(cancellationToken).ConfigureAwait(false);
                return session;
            case LoginType.Ms:
                return await microsoft.LoginAsync(cancellationToken, request.ForceNewLogin).ConfigureAwait(false);
            case LoginType.Nide:
            case LoginType.Auth:
                return await yggdrasil.LoginAsync(request, cancellationToken).ConfigureAwait(false);
            default:
                throw new ArgumentOutOfRangeException(nameof(request), request.Type, "未知登录方式。");
        }
    }
}
