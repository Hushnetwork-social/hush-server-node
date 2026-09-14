using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace HushVoting.IntegrationTests.Infrastructure;

// FEAT-007/008/009 Phase 5: trusted browser keyboard input, never DOM focus/click/fill.
// Locators identify the intended target; only Tab moves focus to it.
internal sealed class HushVotingKeyboard(HushVotingScenario scenario)
{
    public int Activations { get; private set; }
    public int Entries { get; private set; }

    public async Task ReachAsync(ILocator target)
    {
        await Expect(target).ToBeVisibleAsync();
        await Expect(target).ToBeEnabledAsync();
        for (var attempt = 0; attempt < 96; attempt++)
        {
            if (await target.EvaluateAsync<bool>("element => element === document.activeElement")) return;
            await scenario.Page.Keyboard.PressAsync("Tab");
        }
        throw new InvalidOperationException("The required control is not reachable by bounded keyboard traversal.");
    }

    public async Task ActivateAsync(ILocator target, string key = "Enter")
    {
        await ReachAsync(target);
        await scenario.Page.Keyboard.PressAsync(key);
        Activations++;
    }

    public async Task EnterAsync(ILocator target, string value)
    {
        await ReachAsync(target);
        try
        {
            await scenario.Page.Keyboard.PressAsync("ControlOrMeta+A");
            await scenario.Page.Keyboard.TypeAsync(value);
        }
        catch (Exception error) when (error is PlaywrightException or TimeoutException)
        {
            throw new InvalidOperationException("Keyboard credential entry failed; sensitive diagnostics suppressed.");
        }
        Entries++;
    }

    public async Task SelectFileAsync(ILocator trigger, byte[] bytes)
    {
        // Enter must trigger the application's actual file chooser. SetFiles only supplies
        // the public fixture at the OS-dialog boundary; it does not activate the control.
        var chooser = await scenario.Page.RunAndWaitForFileChooserAsync(() => ActivateAsync(trigger));
        try
        {
            await chooser.SetFilesAsync(new FilePayload
            {
                Name = "keyboard-backup.dat", MimeType = "application/octet-stream", Buffer = bytes
            });
        }
        catch (Exception error) when (error is PlaywrightException or TimeoutException)
        {
            throw new InvalidOperationException("Keyboard file handoff failed; fixture diagnostics suppressed.");
        }
    }

    public void AssertExercised(int entries, int activations)
    {
        if (Entries < entries || Activations < activations)
            throw new InvalidOperationException("Keyboard journey did not exercise all required input and action boundaries.");
    }
}
