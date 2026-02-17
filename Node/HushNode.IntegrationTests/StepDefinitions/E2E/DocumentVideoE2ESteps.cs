using FluentAssertions;
using HushNode.IntegrationTests.Infrastructure;
using Microsoft.Playwright;
using TechTalk.SpecFlow;

namespace HushNode.IntegrationTests.StepDefinitions.E2E;

/// <summary>
/// Step definitions for document and video attachment E2E tests.
/// Covers PDF document cards, video thumbnails, mixed type carousels,
/// and blocked file rejection.
/// Tests: F4-E2E-001 through F4-E2E-004 from FEAT-068.
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
    /// Injects a real H.264 MP4 video file (1s, 160x120, blue frame) into the hidden file input.
    /// The browser can decode this and extract frames, so the video-thumbnail path is exercised.
    /// </summary>
    [When(@"(\w+) attaches a video file for ""(.*)"" via file picker")]
    public async Task WhenUserAttachesVideoFileViaFilePicker(string userName, string targetName)
    {
        var page = GetPageForUser(userName);
        var (fileName, mp4Bytes) = TestFileGenerator.GenerateTestVideo(userName, targetName);

        var fileInput = page.Locator("input[data-testid='file-input']").Last;
        await fileInput.SetInputFilesAsync(new FilePayload
        {
            Name = fileName,
            MimeType = "video/mp4",
            Buffer = mp4Bytes,
        });

        Console.WriteLine($"[E2E DocVideo] {userName} attached video: {fileName} ({mp4Bytes.Length} bytes)");
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
