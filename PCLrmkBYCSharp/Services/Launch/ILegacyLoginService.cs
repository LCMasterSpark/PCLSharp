using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services.Launch;

public interface ILegacyLoginService
{
    LoginSession CreateSession(string userName);

    LoginSession CreateSession(string userName, int skinType, bool skinSlim, string skinName, string skinUuid = "");

    void SaveHistory(string userName, IAppSettingsService settings);
}
