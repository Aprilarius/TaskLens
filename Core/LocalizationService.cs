using System.Globalization;
using System.Reflection;
using System.Resources;

namespace TaskLens.Core;

public sealed class LocalizationService
{
    private static readonly HashSet<string> SupportedLanguages = new(StringComparer.OrdinalIgnoreCase)
    {
        "en",
        "ru",
        "az"
    };

    private readonly ResourceManager _resources = new(
        "TaskLens.Localization.Resources",
        Assembly.GetExecutingAssembly());

    public LocalizationService(string configuredLanguage)
    {
        Language = NormalizeLanguage(configuredLanguage);
    }

    public string Language { get; private set; }

    public void SetLanguage(string language)
    {
        Language = NormalizeLanguage(language);
    }

    public string T(string key)
    {
        var selectedCulture = CultureInfo.GetCultureInfo(Language);
        var value = _resources.GetString(key, selectedCulture);
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        value = _resources.GetString(key, CultureInfo.GetCultureInfo("en"));
        return string.IsNullOrWhiteSpace(value) ? key.Replace('_', ' ') : value;
    }

    private static string NormalizeLanguage(string language)
    {
        if (!string.IsNullOrWhiteSpace(language))
        {
            var requested = language.Split('-', StringSplitOptions.RemoveEmptyEntries)[0];
            if (SupportedLanguages.Contains(requested))
            {
                return requested.ToLowerInvariant();
            }
        }

        var systemLanguage = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return SupportedLanguages.Contains(systemLanguage) ? systemLanguage : "en";
    }
}
