using Microsoft.Playwright;

namespace HushVoting.IntegrationTests.Infrastructure;

// FEAT-007 AC-007-068 / FEAT-008 AC-008-079 / FEAT-009 AC-009-084.
// Browser findings only; an automated scan does not establish complete WCAG qualification.
internal sealed class HushVotingAccessibility(HushVotingScenario scenario)
{
    private readonly List<string> _failures = [];
    private readonly HashSet<string> _incompleteRules = [];
    private string? _axeSource;
    private int _checkpoints;

    public async Task CheckAsync(string checkpoint)
    {
        var client = Environment.GetEnvironmentVariable("HUSHVOTING_E2E_CLIENT_ROOT")
            ?? throw new InvalidOperationException("Use the owned HushVoting runner.");
        _axeSource ??= await File.ReadAllTextAsync(Path.Combine(client, "node_modules", "axe-core", "axe.min.js"));
        if (!await scenario.Page.EvaluateAsync<bool>("() => typeof window.axe !== 'undefined'"))
            await scenario.Page.EvaluateAsync(_axeSource);
        try
        {
            foreach (var mode in new[] { "desktop", "mobile-reduced-motion", "forced-colors" })
            {
                await scenario.Page.SetViewportSizeAsync(mode == "mobile-reduced-motion" ? 320 : 1280, 800);
                await scenario.Page.EmulateMediaAsync(new()
                {
                    ReducedMotion = mode == "mobile-reduced-motion" ? ReducedMotion.Reduce : ReducedMotion.NoPreference,
                    ForcedColors = mode == "forced-colors" ? ForcedColors.Active : ForcedColors.None
                });
                // axe mixes non-forced text-fill and forced background colours (#3978).
                // Reproduced with a public black-on-white fixture on this pinned version.
                // Preserve forced-colour contrast as unqualified; all other rules still run.
                if (mode == "forced-colors") _incompleteRules.Add("forced-colors:color-contrast-tool-limitation");
                // Only rule IDs and bounded presentation metadata leave the page.
                // Never serialize axe's HTML, node text, attributes or generated selectors.
                var rules = await scenario.Page.EvaluateAsync<string[]>("""
                    async forcedColors => {
                        const result = await axe.run(document, { runOnly: { type: 'tag', values: ['wcag2a','wcag2aa','wcag21a','wcag21aa','wcag22aa'] },
                            rules: { 'color-contrast': { enabled: !forcedColors } }, resultTypes: ['violations','incomplete'] });
                        const describe = node => {
                            let element;
                            try { element = typeof node.target[0] === 'string' ? document.querySelector(node.target[0]) : null; } catch { return 'unresolved'; }
                            if (!element) return 'unresolved';
                            const known = ['.auth-back-link', '[data-testid="create-action"]', '.button-default', '.auth-heading', '.auth-subtitle'];
                            const owner = known.find(selector => element.matches(selector)) ?? 'other';
                            const style = getComputedStyle(element);
                            const safeColor = value => /^rgba?\([\d., %]+\)$/.test(value) ? value : 'unreported';
                            return owner + ':fg=' + safeColor(style.color) + ':bg=' + safeColor(style.backgroundColor);
                        };
                        return [...result.violations.flatMap(v => [...new Set(v.nodes.map(describe))].map(description => 'violation:' + v.id + '/' + description)),
                            ...result.incomplete.map(v => 'incomplete:' + v.id)];
                    }
                    """, mode == "forced-colors");
                foreach (var rule in rules)
                {
                    if (rule.StartsWith("violation:", StringComparison.Ordinal)) _failures.Add(checkpoint + "/" + mode + "/" + rule);
                    else _incompleteRules.Add(rule);
                }
                var layout = await scenario.Page.EvaluateAsync<int[]>("""
                    () => {
                        const visible = element => { const r = element.getBoundingClientRect(); return r.width > 0 && r.height > 0 && getComputedStyle(element).visibility !== 'hidden'; };
                        const buttons = [...document.querySelectorAll('button')].filter(visible);
                        return [Number(document.documentElement.scrollWidth > innerWidth + 1),
                            buttons.filter(element => { const r = element.getBoundingClientRect(); return r.width < 43.9 || r.height < 43.9; }).length,
                            [...document.querySelectorAll('[tabindex]')].filter(element => visible(element) && element.tabIndex > 0).length];
                    }
                    """);
                if (layout[0] != 0) _failures.Add(checkpoint + "/" + mode + "/horizontal-overflow");
                if (layout[1] != 0) _failures.Add(checkpoint + "/" + mode + "/buttons-below-44px:" + layout[1]);
                if (layout[2] != 0) _failures.Add(checkpoint + "/" + mode + "/positive-tabindex:" + layout[2]);
            }
            _checkpoints++;
        }
        finally
        {
            await scenario.Page.SetViewportSizeAsync(1280, 800);
            await scenario.Page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.NoPreference, ForcedColors = ForcedColors.None });
        }
    }

    public void AssertNoAutomatedFindings()
    {
        if (_checkpoints < 3) throw new InvalidOperationException("Accessibility journey did not reach its required checkpoints.");
        if (_failures.Count > 0)
            throw new InvalidOperationException("Browser accessibility findings: " + string.Join("; ", _failures.Distinct())
                + ". Rules requiring manual review: " + string.Join(',', _incompleteRules));
        // Native prompts, real screen-reader announcements, automatic error focus,
        // zoom and unvisited fault states still require their explicitly mapped evidence.
    }
}
