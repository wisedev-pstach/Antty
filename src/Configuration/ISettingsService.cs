namespace Antty.Configuration;

public interface ISettingsService
{
    Task ShowSettingsMenuAsync(AppConfig config);
}
