using FluentAssertions;
using HushNode.IntegrationTests.Infrastructure;
using Microsoft.Playwright;
using TechTalk.SpecFlow;

namespace HushNode.IntegrationTests.StepDefinitions.E2E;

/// <summary>
/// Step definitions for image attachment E2E tests.
/// Covers composer overlay, file picker attachment, thumbnail display, and lightbox viewer.
/// Tests: F3-003, F3-004, F3-007, F3-008, F3-009, F3-010 from EPIC-001 AcceptanceTests.
/// </summary>
[Binding]
internal sealed class AttachmentE2ESteps : BrowserStepsBase
{
    /// <summary>
    /// Minimal valid 10x10 pixel PNG image for testing.
    /// Small enough for fast processing, valid enough for the browser image pipeline.
    /// The processAttachments pipeline has graceful error handling per step,
    /// so even if thumbnail generation produces odd results for a tiny image,
    /// the message will still send successfully.
    /// </summary>
    private static readonly byte[] TestPngBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAoAAAAKCAYAAACNMs+9AAAAFklEQVQYV2P8z8BQz0AEYBxVOHIUAgBGWAgJ/2JnGgAAAABJRU5ErkJggg==");

    public AttachmentE2ESteps(ScenarioContext scenarioContext) : base(scenarioContext)
    {
    }

    // =========================================================================
    // File Picker & Composer Overlay
    // =========================================================================

    /// <summary>
    /// Injects a test PNG image into the hidden file input in ChatView.
    /// This triggers onChange → openComposer, which opens the ComposerOverlay.
    /// Uses Playwright's SetInputFilesAsync with FilePayload (no temp file needed).
    /// </summary>
    [When(@"(\w+) attaches an image via file picker")]
    public async Task WhenUserAttachesImageViaFilePicker(string userName)
    {
        var page = GetPageForUser(userName);

        // Use CSS selector instead of GetByTestId because the file input has
        // Tailwind's "hidden" class (display:none). GetByTestId waits for
        // actionability/visibility, but SetInputFilesAsync works on hidden inputs
        // when using a CSS selector locator.
        // The page renders two ChatView components (mobile: md:hidden, desktop:
        // hidden md:flex). At 1280x720, the desktop ChatView is visible (.Last).
        // The mobile one (.First) is display:none via md:hidden.
        var fileInput = page.Locator("input[data-testid='file-input']").Last;

        // Inject a test PNG file - this triggers onChange → openComposer
        await fileInput.SetInputFilesAsync(new FilePayload
        {
            Name = "test-image.png",
            MimeType = "image/png",
            Buffer = TestPngBytes,
        });

        Console.WriteLine($"[E2E Attachment] {userName} attached test image via file picker");

        // Allow React state update (composer overlay opening)
        await Task.Delay(500);
    }

    [Then(@"the composer overlay should be visible")]
    public async Task ThenComposerOverlayShouldBeVisible()
    {
        var page = await GetOrCreatePageAsync();
        var overlay = await WaitForTestIdAsync(page, "composer-overlay", 5000);
        (await overlay.IsVisibleAsync()).Should().BeTrue("Composer overlay should be visible after attaching a file");
        Console.WriteLine("[E2E Attachment] Composer overlay is visible");
    }

    [Then(@"the composer overlay should not be visible")]
    public async Task ThenComposerOverlayShouldNotBeVisible()
    {
        var page = await GetOrCreatePageAsync();
        // Allow close animation
        await Task.Delay(500);

        var isVisible = await page.GetByTestId("composer-overlay").IsVisibleAsync();
        isVisible.Should().BeFalse("Composer overlay should not be visible");
        Console.WriteLine("[E2E Attachment] Composer overlay is closed");
    }

    [Then(@"the composer overlay should show an image preview")]
    public async Task ThenComposerOverlayShouldShowImagePreview()
    {
        var page = await GetOrCreatePageAsync();
        var preview = await WaitForTestIdAsync(page, "composer-preview-image", 5000);
        (await preview.IsVisibleAsync()).Should().BeTrue("Image preview should be visible in composer overlay");
        Console.WriteLine("[E2E Attachment] Composer shows image preview");
    }

    [Then(@"the composer send button should be visible")]
    public async Task ThenComposerSendButtonShouldBeVisible()
    {
        var page = await GetOrCreatePageAsync();
        var sendBtn = await WaitForTestIdAsync(page, "composer-send", 5000);
        (await sendBtn.IsVisibleAsync()).Should().BeTrue("Send button should be visible in composer overlay");
    }

    [Then(@"the composer close button should be visible")]
    public async Task ThenComposerCloseButtonShouldBeVisible()
    {
        var page = await GetOrCreatePageAsync();
        var closeBtn = await WaitForTestIdAsync(page, "composer-close", 5000);
        (await closeBtn.IsVisibleAsync()).Should().BeTrue("Close button should be visible in composer overlay");
    }

    [When(@"(\w+) types ""(.*)"" in the composer overlay")]
    public async Task WhenUserTypesInComposerOverlay(string userName, string text)
    {
        var page = GetPageForUser(userName);
        var input = await WaitForTestIdAsync(page, "composer-text-input", 5000);
        await input.FillAsync(text);
        Console.WriteLine($"[E2E Attachment] {userName} typed '{text}' in composer");
    }

    /// <summary>
    /// Closes the composer overlay by clicking the X button.
    /// Verifies F3-004: closing cancels the attachment.
    /// </summary>
    [When(@"(\w+) closes the composer overlay")]
    public async Task WhenUserClosesComposerOverlay(string userName)
    {
        var page = GetPageForUser(userName);
        var closeBtn = await WaitForTestIdAsync(page, "composer-close", 5000);
        await closeBtn.ClickAsync();
        Console.WriteLine($"[E2E Attachment] {userName} closed composer overlay");
        // Allow close animation
        await Task.Delay(500);
    }

    /// <summary>
    /// Sends the message with attachment(s) from the composer overlay.
    /// Handles the full transaction lifecycle:
    /// 1. Starts listening for transactions BEFORE clicking Send
    /// 2. Clicks Send (triggers async attachment processing + TX submission)
    /// 3. Waits for TX to reach mempool
    /// 4. Produces block
    /// 5. Triggers sync for sender
    /// </summary>
    [When(@"(\w+) sends from the composer overlay and waits for confirmation")]
    public async Task WhenUserSendsFromComposerAndWaitsForConfirmation(string userName)
    {
        var page = GetPageForUser(userName);

        // Start listening for the transaction BEFORE clicking Send.
        // The attachment processing pipeline (compress, thumbnail, encrypt) runs
        // asynchronously after the Send click, then submits the transaction.
        var waiter = StartListeningForTransactions(1);

        // Click Send in the composer overlay
        var sendButton = await WaitForTestIdAsync(page, "composer-send", 5000);
        await sendButton.ClickAsync();
        Console.WriteLine($"[E2E Attachment] {userName} clicked Send in composer, waiting for TX...");

        // Wait for transaction (includes attachment processing time) and produce block
        await AwaitTransactionsAndProduceBlockAsync(waiter);
        Console.WriteLine("[E2E Attachment] Block produced with attachment message");

        // Trigger sync for the sender to pick up the confirmed message
        await TriggerSyncAsync(page);
    }

    // =========================================================================
    // Thumbnail Display (F3-009)
    // =========================================================================

    /// <summary>
    /// Verifies that at least one image thumbnail is visible in the chat.
    /// For the receiver, this involves downloading and decrypting the thumbnail
    /// from the server via gRPC streaming, which may take several seconds.
    /// The AttachmentThumbnail component shows a skeleton while loading and
    /// switches to data-testid="attachment-image" when the thumbnail is ready.
    /// </summary>
    [Then(@"(\w+) should see an image thumbnail in the chat")]
    public async Task ThenUserShouldSeeImageThumbnail(string userName)
    {
        var page = GetPageForUser(userName);

        // Wait for at least one image thumbnail to appear.
        // Uses 30s timeout to allow for thumbnail download + decryption.
        var thumbnail = await WaitForTestIdAsync(page, "attachment-image", 30000);
        (await thumbnail.IsVisibleAsync()).Should().BeTrue("Image thumbnail should be visible in chat");
        Console.WriteLine($"[E2E Attachment] {userName} sees image thumbnail in chat");
    }

    /// <summary>
    /// Verifies that the expected number of image thumbnails are visible.
    /// Polls with a timeout to allow for async thumbnail downloads.
    /// </summary>
    [Then(@"(\w+) should see (\d+) image thumbnails in the chat")]
    public async Task ThenUserShouldSeeNThumbnails(string userName, int expectedCount)
    {
        var page = GetPageForUser(userName);

        // Poll until the expected number of thumbnails appear
        var startTime = DateTime.UtcNow;
        var timeout = TimeSpan.FromSeconds(30);

        while (DateTime.UtcNow - startTime < timeout)
        {
            var count = await page.GetByTestId("attachment-image").CountAsync();
            if (count >= expectedCount)
            {
                Console.WriteLine($"[E2E Attachment] {userName} sees {count} image thumbnails (expected >= {expectedCount})");
                return;
            }

            await Task.Delay(500);
        }

        var finalCount = await page.GetByTestId("attachment-image").CountAsync();
        finalCount.Should().BeGreaterOrEqualTo(expectedCount,
            $"Expected at least {expectedCount} image thumbnails but found {finalCount}");
    }

    // =========================================================================
    // Lightbox Viewer (F3-010)
    // =========================================================================

    /// <summary>
    /// Clicks the first visible image thumbnail to open the lightbox.
    /// </summary>
    [When(@"(\w+) clicks an image thumbnail")]
    public async Task WhenUserClicksImageThumbnail(string userName)
    {
        var page = GetPageForUser(userName);
        var thumbnail = await WaitForTestIdAsync(page, "attachment-image", 10000);
        await thumbnail.ClickAsync();
        Console.WriteLine($"[E2E Attachment] {userName} clicked image thumbnail");
        // Allow lightbox to open
        await Task.Delay(500);
    }

    [Then(@"the lightbox overlay should be visible")]
    public async Task ThenLightboxShouldBeVisible()
    {
        var page = await GetOrCreatePageAsync();
        var lightbox = await WaitForTestIdAsync(page, "lightbox-overlay", 10000);
        (await lightbox.IsVisibleAsync()).Should().BeTrue("Lightbox overlay should be visible");
        Console.WriteLine("[E2E Attachment] Lightbox overlay is visible");
    }

    [Then(@"the lightbox overlay should not be visible")]
    public async Task ThenLightboxShouldNotBeVisible()
    {
        var page = await GetOrCreatePageAsync();
        // Allow close animation
        await Task.Delay(500);

        var isVisible = await page.GetByTestId("lightbox-overlay").IsVisibleAsync();
        isVisible.Should().BeFalse("Lightbox overlay should not be visible after closing");
        Console.WriteLine("[E2E Attachment] Lightbox overlay is closed");
    }

    [Then(@"the lightbox close button should be visible")]
    public async Task ThenLightboxCloseButtonShouldBeVisible()
    {
        var page = await GetOrCreatePageAsync();
        var closeBtn = await WaitForTestIdAsync(page, "lightbox-close", 5000);
        (await closeBtn.IsVisibleAsync()).Should().BeTrue("Lightbox close button should be visible");
    }

    /// <summary>
    /// Closes the lightbox by clicking the X button.
    /// </summary>
    [When(@"(\w+) closes the lightbox")]
    public async Task WhenUserClosesLightbox(string userName)
    {
        var page = GetPageForUser(userName);
        var closeButton = await WaitForTestIdAsync(page, "lightbox-close", 5000);
        await closeButton.ClickAsync();
        Console.WriteLine($"[E2E Attachment] {userName} closed lightbox");
        // Allow close animation
        await Task.Delay(500);
    }

    // =========================================================================
    // Labeled Image Steps (for verifiable image identity)
    // =========================================================================

    /// <summary>
    /// Generates a labeled PNG image with text like "Image #1 from Alice to Bob"
    /// and injects it via the hidden file input. The generated file name encodes
    /// the sender, target, and index for later verification via the img alt attribute.
    /// </summary>
    [When(@"(\w+) attaches image (\d+) for ""(.*)"" via file picker")]
    public async Task WhenUserAttachesLabeledImageViaFilePicker(string userName, int imageIndex, string targetName)
    {
        var page = GetPageForUser(userName);

        var (fileName, pngBytes) = TestImageGenerator.GenerateTestAttachment(imageIndex, userName, targetName);

        // Use CSS selector for hidden file input (see WhenUserAttachesImageViaFilePicker for details)
        var fileInput = page.Locator("input[data-testid='file-input']").Last;

        await fileInput.SetInputFilesAsync(new FilePayload
        {
            Name = fileName,
            MimeType = "image/png",
            Buffer = pngBytes,
        });

        Console.WriteLine($"[E2E Attachment] {userName} attached labeled image: {fileName}");

        // Allow React state update (composer overlay opening)
        await Task.Delay(500);
    }

    /// <summary>
    /// Verifies that a specific attachment thumbnail is visible by checking the img alt attribute.
    /// The alt text matches the file name set during generation (e.g., "Image-1-from-Alice-to-Bob.png").
    /// </summary>
    [Then(@"(\w+) should see attachment ""(.*)"" in the thumbnail")]
    public async Task ThenUserShouldSeeAttachmentInThumbnail(string userName, string expectedFileName)
    {
        var page = GetPageForUser(userName);

        // Wait for the specific thumbnail by alt text (30s for download + decryption)
        var thumbnail = page.Locator($"img[data-testid='attachment-img'][alt='{expectedFileName}']");

        try
        {
            await Expect(thumbnail.First).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions
            {
                Timeout = 30000
            });
        }
        catch (Exception)
        {
            // Diagnostic: log all visible attachment alt values on timeout
            var allThumbnails = page.Locator("img[data-testid='attachment-img']");
            var count = await allThumbnails.CountAsync();
            Console.WriteLine($"[E2E Attachment] TIMEOUT - Expected alt='{expectedFileName}', found {count} thumbnails:");
            for (var i = 0; i < count; i++)
            {
                var alt = await allThumbnails.Nth(i).GetAttributeAsync("alt");
                Console.WriteLine($"  [{i}] alt='{alt}'");
            }
            throw;
        }

        Console.WriteLine($"[E2E Attachment] {userName} sees attachment thumbnail: {expectedFileName}");
    }

    /// <summary>
    /// Clicks a specific image thumbnail identified by its alt text (file name).
    /// </summary>
    [When(@"(\w+) clicks the thumbnail for ""(.*)""")]
    public async Task WhenUserClicksThumbnailFor(string userName, string fileName)
    {
        var page = GetPageForUser(userName);

        var thumbnail = page.Locator($"img[data-testid='attachment-img'][alt='{fileName}']");
        await Expect(thumbnail.First).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions
        {
            Timeout = 10000
        });
        await thumbnail.First.ClickAsync();

        Console.WriteLine($"[E2E Attachment] {userName} clicked thumbnail: {fileName}");
        // Allow lightbox to open
        await Task.Delay(500);
    }

    /// <summary>
    /// Verifies that the lightbox is showing a specific image by checking its alt text.
    /// The lightbox first shows a download progress indicator while fetching the full-size
    /// image via gRPC streaming, then renders the img element once download completes.
    /// </summary>
    [Then(@"the lightbox should show attachment ""(.*)""")]
    public async Task ThenLightboxShouldShowAttachment(string expectedFileName)
    {
        var page = await GetOrCreatePageAsync();

        var lightboxImage = page.Locator($"img[data-testid='lightbox-image'][alt='{expectedFileName}']");
        await Expect(lightboxImage).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions
        {
            Timeout = 10000
        });

        Console.WriteLine($"[E2E Attachment] Lightbox shows attachment: {expectedFileName}");
    }

    // =========================================================================
    // Multi-Image Steps (carousel & lightbox navigation)
    // =========================================================================

    /// <summary>
    /// Generates multiple labeled PNG images and injects them all at once via the
    /// hidden file input. Playwright's SetInputFilesAsync accepts an array of FilePayload,
    /// which triggers the onChange handler with all files → openComposer receives them all.
    /// </summary>
    [When(@"(\w+) attaches images (\d+) through (\d+) for ""(.*)"" via file picker")]
    public async Task WhenUserAttachesMultipleLabeledImages(string userName, int startIndex, int endIndex, string targetName)
    {
        var page = GetPageForUser(userName);

        var payloads = new List<FilePayload>();
        for (var i = startIndex; i <= endIndex; i++)
        {
            var (fileName, pngBytes) = TestImageGenerator.GenerateTestAttachment(i, userName, targetName);
            payloads.Add(new FilePayload
            {
                Name = fileName,
                MimeType = "image/png",
                Buffer = pngBytes,
            });
            Console.WriteLine($"[E2E Attachment] Generated: {fileName}");
        }

        var fileInput = page.Locator("input[data-testid='file-input']").Last;
        await fileInput.SetInputFilesAsync(payloads);

        Console.WriteLine($"[E2E Attachment] {userName} attached {payloads.Count} labeled images for {targetName}");
        await Task.Delay(500);
    }

    /// <summary>
    /// Verifies the attachment count indicator in the composer overlay (e.g., "5/5").
    /// </summary>
    [Then(@"the composer should show attachment count ""(.*)""")]
    public async Task ThenComposerShouldShowAttachmentCount(string expectedCount)
    {
        var page = await GetOrCreatePageAsync();
        var countElement = await WaitForTestIdAsync(page, "attachment-count", 5000);
        var text = await countElement.TextContentAsync();
        text.Should().Be(expectedCount, $"Composer attachment count should show '{expectedCount}'");
        Console.WriteLine($"[E2E Attachment] Composer shows attachment count: {text}");
    }

    /// <summary>
    /// Verifies the page indicator in the composer overlay carousel (e.g., "1 / 5").
    /// The ContentCarousel inside ComposerOverlay uses data-testid="page-indicator".
    /// Scoped to the composer-overlay to avoid matching the message carousel's page-indicator.
    /// </summary>
    [Then(@"the composer should show page indicator ""(.*)""")]
    public async Task ThenComposerShouldShowPageIndicator(string expectedIndicator)
    {
        var page = await GetOrCreatePageAsync();
        var overlay = page.GetByTestId("composer-overlay");
        var indicator = overlay.GetByTestId("page-indicator");
        await Expect(indicator).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 5000 });
        var text = await indicator.TextContentAsync();
        text.Should().Be(expectedIndicator, $"Composer page indicator should show '{expectedIndicator}'");
        Console.WriteLine($"[E2E Attachment] Composer page indicator: {text}");
    }

    /// <summary>
    /// Clicks the "Next" arrow in the composer overlay carousel.
    /// Uses aria-label="Next item" scoped to the composer-overlay.
    /// </summary>
    [When(@"(\w+) navigates to the next composer preview")]
    public async Task WhenUserNavigatesToNextComposerPreview(string userName)
    {
        var page = GetPageForUser(userName);
        var overlay = page.GetByTestId("composer-overlay");
        var nextButton = overlay.GetByRole(AriaRole.Button, new() { Name = "Next item" });
        await nextButton.ClickAsync();
        Console.WriteLine($"[E2E Attachment] {userName} clicked Next in composer carousel");
        await Task.Delay(200);
    }

    /// <summary>
    /// Clicks the "Previous" arrow in the composer overlay carousel.
    /// </summary>
    [When(@"(\w+) navigates to the previous composer preview")]
    public async Task WhenUserNavigatesToPreviousComposerPreview(string userName)
    {
        var page = GetPageForUser(userName);
        var overlay = page.GetByTestId("composer-overlay");
        var prevButton = overlay.GetByRole(AriaRole.Button, new() { Name = "Previous item" });
        await prevButton.ClickAsync();
        Console.WriteLine($"[E2E Attachment] {userName} clicked Previous in composer carousel");
        await Task.Delay(200);
    }

    /// <summary>
    /// Verifies the page indicator in the message thumbnail carousel (e.g., "1 / 5").
    /// Scoped to the message-attachments container to avoid matching other carousels.
    /// </summary>
    [Then(@"(\w+) should see thumbnail page indicator ""(.*)""")]
    public async Task ThenUserShouldSeeThumbnailPageIndicator(string userName, string expectedIndicator)
    {
        var page = GetPageForUser(userName);
        var attachments = page.GetByTestId("message-attachments");
        var indicator = attachments.GetByTestId("page-indicator");
        await Expect(indicator.First).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 5000 });
        var text = await indicator.First.TextContentAsync();
        text.Should().Be(expectedIndicator, $"Thumbnail page indicator should show '{expectedIndicator}'");
        Console.WriteLine($"[E2E Attachment] {userName} sees thumbnail page indicator: {text}");
    }

    /// <summary>
    /// Clicks the "Next" arrow in the message thumbnail carousel.
    /// Scoped to message-attachments to avoid matching other carousels.
    /// </summary>
    [When(@"(\w+) clicks the next thumbnail arrow")]
    public async Task WhenUserClicksNextThumbnailArrow(string userName)
    {
        var page = GetPageForUser(userName);
        var attachments = page.GetByTestId("message-attachments");
        var nextButton = attachments.GetByRole(AriaRole.Button, new() { Name = "Next item" });
        await nextButton.First.ClickAsync();
        Console.WriteLine($"[E2E Attachment] {userName} clicked Next in thumbnail carousel");
        await Task.Delay(200);
    }

    /// <summary>
    /// Clicks the "Previous" arrow in the message thumbnail carousel.
    /// </summary>
    [When(@"(\w+) clicks the previous thumbnail arrow")]
    public async Task WhenUserClicksPreviousThumbnailArrow(string userName)
    {
        var page = GetPageForUser(userName);
        var attachments = page.GetByTestId("message-attachments");
        var prevButton = attachments.GetByRole(AriaRole.Button, new() { Name = "Previous item" });
        await prevButton.First.ClickAsync();
        Console.WriteLine($"[E2E Attachment] {userName} clicked Previous in thumbnail carousel");
        await Task.Delay(200);
    }

    /// <summary>
    /// Clicks the currently displayed thumbnail image in the carousel.
    /// Uses the carousel-item container to scope to the visible item.
    /// </summary>
    [When(@"(\w+) clicks the current thumbnail image")]
    public async Task WhenUserClicksCurrentThumbnailImage(string userName)
    {
        var page = GetPageForUser(userName);
        var attachments = page.GetByTestId("message-attachments");
        var currentItem = attachments.GetByTestId("carousel-item").First;
        var image = currentItem.GetByTestId("attachment-image");
        await image.ClickAsync();
        Console.WriteLine($"[E2E Attachment] {userName} clicked current thumbnail image");
        await Task.Delay(500);
    }

    /// <summary>
    /// Verifies the lightbox page indicator (e.g., "1 / 5").
    /// </summary>
    [Then(@"the lightbox should show page indicator ""(.*)""")]
    public async Task ThenLightboxShouldShowPageIndicator(string expectedIndicator)
    {
        var page = await GetOrCreatePageAsync();
        var indicator = page.GetByTestId("lightbox-page-indicator");
        await Expect(indicator).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 5000 });
        var text = await indicator.TextContentAsync();
        text.Should().Be(expectedIndicator, $"Lightbox page indicator should show '{expectedIndicator}'");
        Console.WriteLine($"[E2E Attachment] Lightbox page indicator: {text}");
    }

    /// <summary>
    /// Clicks the "Next" arrow in the lightbox viewer.
    /// </summary>
    [When(@"(\w+) clicks the next lightbox arrow")]
    public async Task WhenUserClicksNextLightboxArrow(string userName)
    {
        var page = GetPageForUser(userName);
        var nextButton = page.GetByTestId("lightbox-next");
        await nextButton.ClickAsync();
        Console.WriteLine($"[E2E Attachment] {userName} clicked Next in lightbox");
        // Allow image download + render
        await Task.Delay(500);
    }

    /// <summary>
    /// Clicks the "Previous" arrow in the lightbox viewer.
    /// </summary>
    [When(@"(\w+) clicks the previous lightbox arrow")]
    public async Task WhenUserClicksPreviousLightboxArrow(string userName)
    {
        var page = GetPageForUser(userName);
        var prevButton = page.GetByTestId("lightbox-prev");
        await prevButton.ClickAsync();
        Console.WriteLine($"[E2E Attachment] {userName} clicked Previous in lightbox");
        // Allow image download + render
        await Task.Delay(500);
    }

    /// <summary>
    /// Verifies which image is currently displayed in the thumbnail carousel
    /// by checking the alt attribute of the img inside the active carousel-item.
    /// </summary>
    [Then(@"the current thumbnail should show ""(.*)""")]
    public async Task ThenCurrentThumbnailShouldShow(string expectedFileName)
    {
        var page = await GetOrCreatePageAsync();
        var attachments = page.GetByTestId("message-attachments");
        var currentItem = attachments.GetByTestId("carousel-item").First;
        var img = currentItem.Locator("img[data-testid='attachment-img']");
        await Expect(img).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 5000 });
        var alt = await img.GetAttributeAsync("alt");
        alt.Should().Be(expectedFileName, $"Current thumbnail should show '{expectedFileName}'");
        Console.WriteLine($"[E2E Attachment] Current thumbnail shows: {alt}");
    }

    /// <summary>
    /// Generates a labeled animated GIF (400x200, 3 frames cycling background colors)
    /// with text "Animated #N from Sender to Target" and injects it via the hidden
    /// file input. Tests the full animated GIF pipeline: compression bypass, thumbnail
    /// (static first frame), lightbox (animated).
    /// </summary>
    [When(@"(\w+) attaches animated GIF (\d+) for ""(.*)"" via file picker")]
    public async Task WhenUserAttachesAnimatedGifViaFilePicker(string userName, int gifIndex, string targetName)
    {
        var page = GetPageForUser(userName);

        var (fileName, gifBytes) = TestImageGenerator.GenerateAnimatedTestGif(gifIndex, userName, targetName);

        var fileInput = page.Locator("input[data-testid='file-input']").Last;

        await fileInput.SetInputFilesAsync(new FilePayload
        {
            Name = fileName,
            MimeType = "image/gif",
            Buffer = gifBytes,
        });

        Console.WriteLine($"[E2E Attachment] {userName} attached animated GIF: {fileName} ({gifBytes.Length} bytes)");
        await Task.Delay(500);
    }

    /// <summary>
    /// Generates multiple labeled animated GIFs and injects them all at once via the
    /// hidden file input, same pattern as multi-image attachment.
    /// </summary>
    [When(@"(\w+) attaches animated GIFs (\d+) through (\d+) for ""(.*)"" via file picker")]
    public async Task WhenUserAttachesMultipleAnimatedGifs(string userName, int startIndex, int endIndex, string targetName)
    {
        var page = GetPageForUser(userName);

        var payloads = new List<FilePayload>();
        for (var i = startIndex; i <= endIndex; i++)
        {
            var (fileName, gifBytes) = TestImageGenerator.GenerateAnimatedTestGif(i, userName, targetName);
            payloads.Add(new FilePayload
            {
                Name = fileName,
                MimeType = "image/gif",
                Buffer = gifBytes,
            });
            Console.WriteLine($"[E2E Attachment] Generated animated GIF: {fileName}");
        }

        var fileInput = page.Locator("input[data-testid='file-input']").Last;
        await fileInput.SetInputFilesAsync(payloads);

        Console.WriteLine($"[E2E Attachment] {userName} attached {payloads.Count} animated GIFs for {targetName}");
        await Task.Delay(500);
    }

    /// <summary>
    /// Verifies which image is currently displayed in the composer overlay preview
    /// by checking the alt attribute of the visible composer-preview-image.
    /// Works for both single-image (no carousel) and multi-image (carousel) cases,
    /// because ContentCarousel only renders the current child.
    /// </summary>
    [Then(@"the current composer preview should show ""(.*)""")]
    public async Task ThenCurrentComposerPreviewShouldShow(string expectedFileName)
    {
        var page = await GetOrCreatePageAsync();
        var overlay = page.GetByTestId("composer-overlay");
        var img = overlay.Locator("img[data-testid='composer-preview-image']");
        await Expect(img.First).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 5000 });
        var alt = await img.First.GetAttributeAsync("alt");
        alt.Should().Be(expectedFileName, $"Current composer preview should show '{expectedFileName}'");
        Console.WriteLine($"[E2E Attachment] Current composer preview shows: {alt}");
    }

    /// <summary>
    /// Simulates pasting an image from the clipboard into the composer overlay.
    /// Generates a labeled PNG, creates a synthetic ClipboardEvent with the image
    /// as a File in clipboardData, and dispatches it on the composer text input.
    /// This triggers the onPaste handler → onImagePaste → handleImagePaste in ChatView.
    /// </summary>
    [When(@"(\w+) pastes image (\d+) for ""(.*)"" into the composer")]
    public async Task WhenUserPastesImageIntoComposer(string userName, int imageIndex, string targetName)
    {
        var page = GetPageForUser(userName);

        var (fileName, pngBytes) = TestImageGenerator.GenerateTestAttachment(imageIndex, userName, targetName);
        var base64 = Convert.ToBase64String(pngBytes);

        // Dispatch a synthetic paste event with the image as clipboard data
        await page.EvaluateAsync(@"(args) => {
            const binary = atob(args.base64);
            const bytes = new Uint8Array(binary.length);
            for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);

            const blob = new Blob([bytes], { type: 'image/png' });
            const file = new File([blob], args.fileName, { type: 'image/png' });

            const dt = new DataTransfer();
            dt.items.add(file);

            const event = new ClipboardEvent('paste', { bubbles: true, cancelable: true });
            Object.defineProperty(event, 'clipboardData', { value: dt });

            const input = document.querySelector('[data-testid=""composer-text-input""]');
            if (input) input.dispatchEvent(event);
        }", new { base64, fileName });

        Console.WriteLine($"[E2E Attachment] {userName} pasted labeled image into composer: {fileName}");
        await Task.Delay(500);
    }

    /// <summary>
    /// Presses the Right arrow key on the keyboard.
    /// Used for lightbox navigation (ArrowRight advances to next image).
    /// </summary>
    [When(@"(\w+) presses the right arrow key")]
    public async Task WhenUserPressesRightArrowKey(string userName)
    {
        var page = GetPageForUser(userName);
        await page.Keyboard.PressAsync("ArrowRight");
        Console.WriteLine($"[E2E Attachment] {userName} pressed Right arrow key");
        await Task.Delay(500);
    }

    /// <summary>
    /// Presses the Left arrow key on the keyboard.
    /// Used for lightbox navigation (ArrowLeft goes to previous image).
    /// </summary>
    [When(@"(\w+) presses the left arrow key")]
    public async Task WhenUserPressesLeftArrowKey(string userName)
    {
        var page = GetPageForUser(userName);
        await page.Keyboard.PressAsync("ArrowLeft");
        Console.WriteLine($"[E2E Attachment] {userName} pressed Left arrow key");
        await Task.Delay(500);
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    /// <summary>
    /// Gets the page for a specific user from ScenarioContext.
    /// Switches the active page context to the specified user
    /// (required for auto-screenshots in ScenarioHooks).
    /// </summary>
    private IPage GetPageForUser(string userName)
    {
        var key = $"E2E_Page_{userName}";
        if (ScenarioContext.TryGetValue(key, out var pageObj) && pageObj is IPage page)
        {
            ScenarioContext["E2E_MainPage"] = page;
            ScenarioContext["CurrentUser"] = userName;
            return page;
        }

        throw new InvalidOperationException($"No browser page found for user '{userName}'");
    }

    private static ILocatorAssertions Expect(ILocator locator)
    {
        return Assertions.Expect(locator);
    }
}
