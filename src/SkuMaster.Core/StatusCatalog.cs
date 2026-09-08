namespace SkuMaster.Core;

public static class StatusCatalog
{
    public static IReadOnlyList<string> Values { get; } = Array.AsReadOnly(new[]
    {
        "Снят с производства", "Топ продаж", "Лучшая цена", "Скоро в продаже", "Новинка",
        "Уценка", "%", "Спецпредложение", "Временно недоступен"
    });
    public static bool IsAllowed(string? value, bool allowEmpty = true)
        => value is not null && ((allowEmpty && value.Length == 0) || Values.Contains(value, StringComparer.Ordinal));
    public static void Validate(string? value, bool allowEmpty = true)
    {
        if (!IsAllowed(value, allowEmpty)) throw new ArgumentException("Оберіть статус із дозволеного списку" + (allowEmpty ? " або «Без статусу»." : "."));
    }
    public static void ValidateRules(RuleSettings rules)
    {
        Validate(rules.UnavailableStatus, false);
        Validate(rules.MatchStatus);
        Validate(rules.ReplacementStatus);
    }
}
