using FluentAssertions;
using HushNode.IntegrationTests.Infrastructure;
using Microsoft.Playwright;
using TechTalk.SpecFlow;

namespace HushNode.IntegrationTests.StepDefinitions.E2E;

/// <summary>
/// Step definitions for document and video attachment E2E tests.
/// Covers PDF document cards, video thumbnails, mixed type carousels,
/// and blocked file rejection.
/// Tests: F4-E2E-001 through F4-E2E-005 from FEAT-068.
/// </summary>
[Binding]
internal sealed class DocumentVideoE2ESteps : BrowserStepsBase
{
    public DocumentVideoE2ESteps(ScenarioContext scenarioContext) : base(scenarioContext)
    {
    }

    // =========================================================================
    // File Picker Steps (PDF, Video, Mixed, Blocked)
    // =========================================================================

    /// <summary>
    /// Injects a minimal valid PDF file into the hidden file input.
    /// The file name encodes sender and target for verification.
    /// </summary>
    [When(@"(\w+) attaches a PDF document for ""(.*)"" via file picker")]
    public async Task WhenUserAttachesPdfDocumentViaFilePicker(string userName, string targetName)
    {
        var page = GetPageForUser(userName);
        var (fileName, pdfBytes) = TestFileGenerator.GenerateTestPdf(userName, targetName);

        var fileInput = page.Locator("input[data-testid='file-input']").Last;
        await fileInput.SetInputFilesAsync(new FilePayload
        {
            Name = fileName,
            MimeType = "application/pdf",
            Buffer = pdfBytes,
        });

        Console.WriteLine($"[E2E DocVideo] {userName} attached PDF: {fileName} ({pdfBytes.Length} bytes)");
        await Task.Delay(500);
    }

    /// <summary>
    /// Injects a real VP8/WebM video file (1s, 160x120, blue frame) into the hidden file input.
    /// WebM/VP8 is used because Playwright's Chromium always supports VP8 (open codec).
    /// The browser can decode this and extract frames, so the video-thumbnail path is exercised.
    /// </summary>
    [When(@"(\w+) attaches a video file for ""(.*)"" via file picker")]
    public async Task WhenUserAttachesVideoFileViaFilePicker(string userName, string targetName)
    {
        var page = GetPageForUser(userName);
        var (fileName, videoBytes) = TestFileGenerator.GenerateTestVideo(userName, targetName);

        var fileInput = page.Locator("input[data-testid='file-input']").Last;
        await fileInput.SetInputFilesAsync(new FilePayload
        {
            Name = fileName,
            MimeType = "video/webm",
            Buffer = videoBytes,
        });

        Console.WriteLine($"[E2E DocVideo] {userName} attached video: {fileName} ({videoBytes.Length} bytes)");
        // Video frame extraction runs asynchronously - allow time
        await Task.Delay(1500);
    }

    /// <summary>
    /// Injects an image and a PDF file together into the hidden file input.
    /// Uses SetInputFilesAsync with an array of FilePayload.
    /// </summary>
    [When(@"(\w+) attaches an image and a PDF for ""(.*)"" via file picker")]
    public async Task WhenUserAttachesImageAndPdfViaFilePicker(string userName, string targetName)
    {
        var page = GetPageForUser(userName);

        // Generate image (reuse TestImageGenerator)
        var (imgFileName, imgBytes) = TestImageGenerator.GenerateTestAttachment(1, userName, targetName);

        // Generate PDF
        var (pdfFileName, pdfBytes) = TestFileGenerator.GenerateTestPdf(userName, targetName);

        var fileInput = page.Locator("input[data-testid='file-input']").Last;
        await fileInput.SetInputFilesAsync(new[]
        {
            new FilePayload { Name = imgFileName, MimeType = "image/png", Buffer = imgBytes },
            new FilePayload { Name = pdfFileName, MimeType = "application/pdf", Buffer = pdfBytes },
        });

        Console.WriteLine($"[E2E DocVideo] {userName} attached image ({imgFileName}) + PDF ({pdfFileName})");
        await Task.Delay(500);
    }

    /// <summary>
    /// Tries to inject a blocked executable file (.exe) into the file input.
    /// The file picker's accept filter should prevent this, but we also test
    /// the runtime validation that shows a toast notification.
    /// Playwright's SetInputFilesAsync bypasses the accept filter, so the
    /// runtime validation in ChatView is tested.
    /// </summary>
    [When(@"(\w+) tries to attach a blocked executable file")]
    public async Task WhenUserTriesToAttachBlockedExecutableFile(string userName)
    {
        var page = GetPageForUser(userName);
        var (fileName, exeBytes) = TestFileGenerator.GenerateBlockedExe();

        var fileInput = page.Locator("input[data-testid='file-input']").Last;
        await fileInput.SetInputFilesAsync(new FilePayload
        {
            Name = fileName,
            MimeType = "application/x-msdownload",
            Buffer = exeBytes,
        });

        Console.WriteLine($"[E2E DocVideo] {userName} tried to attach blocked file: {fileName}");
        // Allow time for validation and toast to appear
        await Task.Delay(1000);
    }

    // =========================================================================
    // Composer Overlay Verification
    // =========================================================================

    /// <summary>
    /// Verifies the composer overlay shows a document preview (icon-based).
    /// Matches both generic document preview and PDF thumbnail preview.
    /// </summary>
    [Then(@"the composer overlay should show a document preview")]
    public async Task ThenComposerOverlayShouldShowDocumentPreview()
    {
        var page = await GetOrCreatePageAsync();

        // Check for any document preview type: generic file, PDF thumbnail, or PDF skeleton
        var startTime = DateTime.UtcNow;
        var timeout = TimeSpan.FromSeconds(10);

        while (DateTime.UtcNow - startTime < timeout)
        {
            var isFilePreview = await page.GetByTestId("composer-preview-file").IsVisibleAsync();
            var isPdfThumb = await page.GetByTestId("composer-pdf-thumbnail").IsVisibleAsync();
            var isPdfSkeleton = await page.GetByTestId("composer-pdf-skeleton").IsVisibleAsync();

            if (isFilePreview || isPdfThumb || isPdfSkeleton)
            {
                Console.WriteLine($"[E2E DocVideo] Composer shows document preview (file={isFilePreview}, pdfThumb={isPdfThumb}, pdfSkeleton={isPdfSkeleton})");
                return;
            }

            await Task.Delay(200);
        }

        // Fail with diagnostic info
        throw new Exception("Composer overlay does not show any document preview " +
            "(expected composer-preview-file, composer-pdf-thumbnail, or composer-pdf-skeleton)");
    }

    // =========================================================================
    // Message Bubble Verification (Document Card, Video Thumbnail)
    // =========================================================================

    /// <summary>
    /// Verifies that at least one document card is visible in the chat.
    /// Document cards are rendered by AttachmentThumbnail → DocumentCard for
    /// non-image, non-video MIME types.
    /// Uses 30s timeout to allow for attachment download + decryption.
    /// </summary>
    [Then(@"(\w+) should see a document card in the message")]
    public async Task ThenUserShouldSeeDocumentCard(string userName)
    {
        var page = GetPageForUser(userName);

        var docCard = await WaitForTestIdAsync(page, "document-card", 30000);
        (await docCard.IsVisibleAsync()).Should().BeTrue("Document card should be visible in chat message");
        Console.WriteLine($"[E2E DocVideo] {userName} sees document card in message");
    }

    /// <summary>
    /// Verifies the document card shows the expected filename.
    /// Checks the document-name testid within the document-card.
    /// </summary>
    [Then(@"the document card should show filename ""(.*)""")]
    public async Task ThenDocumentCardShouldShowFilename(string expectedFilename)
    {
        var page = await GetOrCreatePageAsync();

        var docName = await WaitForTestIdAsync(page, "document-name", 10000);
        var text = await docName.TextContentAsync();
        text.Should().Be(expectedFilename,
            $"Document card filename should be '{expectedFilename}' but was '{text}'");
        Console.WriteLine($"[E2E DocVideo] Document card shows filename: {text}");
    }

    /// <summary>
    /// Verifies that a video element (thumbnail or fallback) is visible in the chat.
    /// VideoThumbnail renders either:
    /// - data-testid="video-thumbnail" when a frame was extracted
    /// - data-testid="video-fallback" when extraction failed
    /// - data-testid="video-skeleton" while loading
    /// Uses 30s timeout to allow for attachment download + decryption.
    /// </summary>
    [Then(@"(\w+) should see a video element in the message")]
    public async Task ThenUserShouldSeeVideoElement(string userName)
    {
        var page = GetPageForUser(userName);

        // Poll for either video-thumbnail or video-fallback
        var startTime = DateTime.UtcNow;
        var timeout = TimeSpan.FromSeconds(30);

        while (DateTime.UtcNow - startTime < timeout)
        {
            var hasThumbnail = await page.GetByTestId("video-thumbnail").CountAsync() > 0
                && await page.GetByTestId("video-thumbnail").First.IsVisibleAsync();
            var hasFallback = await page.GetByTestId("video-fallback").CountAsync() > 0
                && await page.GetByTestId("video-fallback").First.IsVisibleAsync();

            if (hasThumbnail || hasFallback)
            {
                Console.WriteLine($"[E2E DocVideo] {userName} sees video element (thumbnail={hasThumbnail}, fallback={hasFallback})");
                return;
            }

            await Task.Delay(500);
        }

        // Fail with diagnostic info
        var thumbCount = await page.GetByTestId("video-thumbnail").CountAsync();
        var fallbackCount = await page.GetByTestId("video-fallback").CountAsync();
        var skeletonCount = await page.GetByTestId("video-skeleton").CountAsync();
        throw new Exception(
            $"No video element visible for {userName} after 30s. " +
            $"Counts: video-thumbnail={thumbCount}, video-fallback={fallbackCount}, video-skeleton={skeletonCount}");
    }

    // =========================================================================
    // Lightbox Video Player Steps (F4-E2E-005)
    // =========================================================================

    /// <summary>
    /// Clicks the video thumbnail or fallback element in the message to open the lightbox.
    /// Handles both video-thumbnail (extracted frame) and video-fallback (no frame).
    /// </summary>
    [When(@"(\w+) clicks the video element in the message")]
    public async Task WhenUserClicksVideoElementInMessage(string userName)
    {
        var page = GetPageForUser(userName);

        // Try video-thumbnail first, then video-fallback
        var thumbnail = page.GetByTestId("video-thumbnail");
        var fallback = page.GetByTestId("video-fallback");

        if (await thumbnail.CountAsync() > 0 && await thumbnail.First.IsVisibleAsync())
        {
            await thumbnail.First.ClickAsync();
            Console.WriteLine($"[E2E DocVideo] {userName} clicked video-thumbnail to open lightbox");
        }
        else if (await fallback.CountAsync() > 0 && await fallback.First.IsVisibleAsync())
        {
            await fallback.First.ClickAsync();
            Console.WriteLine($"[E2E DocVideo] {userName} clicked video-fallback to open lightbox");
        }
        else
        {
            throw new Exception($"No video element (thumbnail or fallback) found for {userName} to click");
        }

        // Allow lightbox to open and start downloading full video
        await Task.Delay(1000);
    }

    /// <summary>
    /// Verifies that the video player component is visible in the lightbox.
    /// The VideoPlayer renders when the full video blob URL is available.
    /// Uses 30s timeout for full video download + decryption.
    /// </summary>
    [Then(@"the lightbox should show a video player")]
    public async Task ThenLightboxShouldShowVideoPlayer()
    {
        var page = await GetOrCreatePageAsync();
        var player = await WaitForTestIdAsync(page, "video-player", 30000);
        (await player.IsVisibleAsync()).Should().BeTrue("Video player should be visible in lightbox");
        Console.WriteLine("[E2E DocVideo] Lightbox shows video player");
    }

    /// <summary>
    /// Verifies that the video progress bar is visible in the player.
    /// </summary>
    [Then(@"the video progress bar should be visible")]
    public async Task ThenVideoProgressBarShouldBeVisible()
    {
        var page = await GetOrCreatePageAsync();
        var progressBar = await WaitForTestIdAsync(page, "video-progress-bar", 5000);
        (await progressBar.IsVisibleAsync()).Should().BeTrue("Video progress bar should be visible");
        Console.WriteLine("[E2E DocVideo] Video progress bar is visible");
    }

    /// <summary>
    /// Verifies that the video time displays are visible (current time + duration).
    /// The duration may show "--:--" if the browser can't decode the video metadata
    /// (e.g., Playwright's Chromium without H.264 codec), but the UI elements must exist.
    /// </summary>
    [Then(@"the video time displays should be visible")]
    public async Task ThenVideoTimeDisplaysShouldBeVisible()
    {
        var page = await GetOrCreatePageAsync();

        var currentTimeEl = await WaitForTestIdAsync(page, "video-current-time", 5000);
        (await currentTimeEl.IsVisibleAsync()).Should().BeTrue("Current time display should be visible");
        var currentTimeText = await currentTimeEl.TextContentAsync();

        var durationEl = await WaitForTestIdAsync(page, "video-duration", 5000);
        (await durationEl.IsVisibleAsync()).Should().BeTrue("Duration display should be visible");
        var durationText = await durationEl.TextContentAsync();

        Console.WriteLine($"[E2E DocVideo] Video time displays visible: {currentTimeText} / {durationText}");
    }

    /// <summary>
    /// Clicks the play icon overlay to start playback.
    /// The play icon has pointer-events:auto so it's directly clickable by Playwright,
    /// and the click counts as a trusted user gesture for autoplay policy.
    /// </summary>
    [When(@"(\w+) clicks the video to play")]
    public async Task WhenUserClicksVideoToPlay(string userName)
    {
        var page = GetPageForUser(userName);

        // Scope to lightbox to avoid matching the thumbnail's play icon
        var lightbox = page.GetByTestId("lightbox-overlay");
        var playIcon = lightbox.GetByTestId("video-play-icon");
        await playIcon.ClickAsync();
        Console.WriteLine($"[E2E DocVideo] {userName} clicked play icon to start playback");
        await Task.Delay(500);
    }

    /// <summary>
    /// Verifies the video is in the playing state by checking the state testid.
    /// </summary>
    [Then(@"the video should be playing")]
    public async Task ThenVideoShouldBePlaying()
    {
        var page = await GetOrCreatePageAsync();

        var startTime = DateTime.UtcNow;
        var timeout = TimeSpan.FromSeconds(5);

        while (DateTime.UtcNow - startTime < timeout)
        {
            var isPlaying = await page.GetByTestId("video-state-playing").CountAsync() > 0;
            if (isPlaying)
            {
                Console.WriteLine("[E2E DocVideo] Video is playing (state-playing testid found)");
                return;
            }

            // Also check via JavaScript as a secondary signal
            var jsPaused = await page.EvalOnSelectorAsync<bool>(
                "[data-testid='video-player-element']", "el => el.paused");
            if (!jsPaused)
            {
                Console.WriteLine("[E2E DocVideo] Video is playing (JS paused=false, waiting for React state)");
                await Task.Delay(200);
                continue;
            }

            await Task.Delay(200);
        }

        // Diagnostic: check if video is paused or if player exists
        var hasPaused = await page.GetByTestId("video-state-paused").CountAsync() > 0;
        var hasPlayer = await page.GetByTestId("video-player").CountAsync() > 0;
        var videoError = await page.EvalOnSelectorAsync<string?>(
            "[data-testid='video-player-element']",
            "el => el.error ? el.error.message : null");
        throw new Exception(
            $"Video is not playing after 5s. " +
            $"video-state-paused={hasPaused}, video-player={hasPlayer}, videoError={videoError}");
    }

    /// <summary>
    /// Verifies the Pause icon is visible in the video player overlay.
    /// </summary>
    [Then(@"the video pause icon should be visible")]
    public async Task ThenVideoPauseIconShouldBeVisible()
    {
        var page = await GetOrCreatePageAsync();
        var pauseIcon = await WaitForTestIdAsync(page, "video-pause-icon", 5000);
        (await pauseIcon.IsVisibleAsync()).Should().BeTrue("Pause icon should be visible when video is playing");
        Console.WriteLine("[E2E DocVideo] Video pause icon is visible");
    }

    /// <summary>
    /// Clicks the pause icon overlay to pause playback.
    /// The pause icon has pointer-events:auto so it's directly clickable.
    /// </summary>
    [When(@"(\w+) clicks the video to pause")]
    public async Task WhenUserClicksVideoToPause(string userName)
    {
        var page = GetPageForUser(userName);

        // Scope to lightbox to avoid any ambiguity
        var lightbox = page.GetByTestId("lightbox-overlay");
        var pauseIcon = lightbox.GetByTestId("video-pause-icon");
        await pauseIcon.ClickAsync();
        Console.WriteLine($"[E2E DocVideo] {userName} clicked pause icon to pause playback");
        await Task.Delay(500);
    }

    /// <summary>
    /// Verifies the video is in the paused state by checking the state testid.
    /// </summary>
    [Then(@"the video should be paused")]
    public async Task ThenVideoShouldBePaused()
    {
        var page = await GetOrCreatePageAsync();

        var startTime = DateTime.UtcNow;
        var timeout = TimeSpan.FromSeconds(5);

        while (DateTime.UtcNow - startTime < timeout)
        {
            var isPaused = await page.GetByTestId("video-state-paused").CountAsync() > 0;
            if (isPaused)
            {
                Console.WriteLine("[E2E DocVideo] Video is paused");
                return;
            }

            await Task.Delay(200);
        }

        throw new Exception("Video is not paused after 5s");
    }

    /// <summary>
    /// Verifies the Play icon is visible in the video player overlay (shown when paused).
    /// Named differently from the thumbnail play icon to avoid step binding conflicts.
    /// </summary>
    [Then(@"the video play icon should be visible in the player")]
    public async Task ThenVideoPlayIconShouldBeVisibleInPlayer()
    {
        var page = await GetOrCreatePageAsync();

        // The play icon in the video player (not the thumbnail)
        var lightbox = page.GetByTestId("lightbox-overlay");
        var playIcon = lightbox.GetByTestId("video-play-icon");

        await Assertions.Expect(playIcon).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 5000 });

        Console.WriteLine("[E2E DocVideo] Video play icon is visible in player (video is paused)");
    }

    /// <summary>
    /// Clicks the progress bar at its left edge to seek to the beginning.
    /// Uses Playwright's click with position option to click at x=5 (near left edge).
    /// </summary>
    [When(@"(\w+) clicks the progress bar at the beginning")]
    public async Task WhenUserClicksProgressBarAtBeginning(string userName)
    {
        var page = GetPageForUser(userName);
        var progressBar = await WaitForTestIdAsync(page, "video-progress-bar", 5000);

        // Click near the left edge of the progress bar (position x=5)
        await progressBar.ClickAsync(new LocatorClickOptions
        {
            Position = new Position { X = 5, Y = 5 }
        });

        Console.WriteLine($"[E2E DocVideo] {userName} clicked progress bar at the beginning");
        await Task.Delay(300);
    }

    /// <summary>
    /// Verifies the video current time is near zero (within 0.5s).
    /// Uses JavaScript evaluation to read the actual video.currentTime.
    /// </summary>
    [Then(@"the video current time should be near zero")]
    public async Task ThenVideoCurrentTimeShouldBeNearZero()
    {
        var page = await GetOrCreatePageAsync();

        var currentTime = await page.EvalOnSelectorAsync<double>(
            "[data-testid='video-player-element']",
            "el => el.currentTime");

        currentTime.Should().BeLessThan(0.5,
            $"Video current time should be near zero after seeking to beginning, but was {currentTime:F2}s");
        Console.WriteLine($"[E2E DocVideo] Video current time is {currentTime:F2}s (near zero)");
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    /// <summary>
    /// Gets the page for a specific user from ScenarioContext.
    /// Switches the active page context to the specified user.
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
}
