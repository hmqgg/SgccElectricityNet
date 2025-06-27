using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using Nito.AsyncEx;
using SgccElectricityNet.Worker.Models;
using SgccElectricityNet.Worker.Services.Captcha;

namespace SgccElectricityNet.Worker.Services.Fetcher;

public sealed class PlaywrightFetcherService(
    IOptions<FetcherOptions> options,
    PlaywrightBrowserFactory browserFactory,
    ICaptchaService captchaService,
    ILogger<PlaywrightFetcherService> logger)
    : IFetcherService
{
    private const string LoginUrl = "https://www.95598.cn/osgweb/login";
    private const string RedirectUrl = "https://www.95598.cn/osgweb/my95598";
    // Yes, it's 'Maneger'.
    private const string DoorNumberUrl = "https://www.95598.cn/osgweb/doorNumberManeger";
    private const string BalanceUrl = "https://www.95598.cn/osgweb/userAcc";
    private const string ElectricUsageUrl = "https://www.95598.cn/osgweb/electricityCharge";

    private readonly string _username = options.Value.Username ?? throw new ArgumentNullException(nameof(FetcherOptions.Username), "SGCC Username is not configured.");
    private readonly string? _password = options.Value.Password;
    private readonly int _maxAttempts = options.Value.MaxAttempts;

    public async Task<ElectricityData> FetchAsync(string userId, CancellationToken cancellationToken = default)
    {
        var page = await NewPageAsync();
        try
        {
            var loggedIn = await LoginAsync(page, cancellationToken);
            if (!loggedIn)
            {
                logger.LogError("Failed to login after multiple attempts.");
                throw new InvalidOperationException("Failed to login with multiple retries.");
            }

            var userIds = await GetUserIdsAsync(page, cancellationToken);
            logger.LogInformation("Found user IDs: {userIds}", string.Join(", ", userIds.Keys));

            var targetUserId = userIds.Keys.FirstOrDefault(id => id == userId);
            if (targetUserId is null)
            {
                throw new Exception($"Failed to found user ID: {userId}");
            }

            logger.LogInformation("Fetching data for user ID: {userId}", targetUserId);
            return await GetElectricityDataForUserAsync(page, targetUserId, userIds[targetUserId], cancellationToken);
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    public async Task<IEnumerable<ElectricityData>> FetchAllAsync(CancellationToken cancellationToken = default)
    {
        var page = await NewPageAsync();
        try
        {
            var loggedIn = await LoginAsync(page, cancellationToken);
            if (!loggedIn)
            {
                logger.LogError("Failed to login after multiple attempts.");
                throw new InvalidOperationException("Failed to login with multiple retries.");
            }

            var userIds = await GetUserIdsAsync(page, cancellationToken);
            logger.LogInformation("Found {count} user IDs: {userIds}", userIds.Count, string.Join(", ", userIds));

            var allData = new List<ElectricityData>();
            foreach (var (userId, userMatch) in userIds)
            {
                logger.LogInformation("Fetching data for user: {name}, {userId}", userMatch, userId);
                var data = await GetElectricityDataForUserAsync(page, userId, userMatch, cancellationToken);
                allData.Add(data);
            }

            return allData;
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    private Task<IPage> NewPageAsync()
    {
        // Call CloseAsync in finally block to ensure the page is closed properly.
        return browserFactory.NewPageAsync();
    }

    private async Task<bool> LoginAsync(IPage page, CancellationToken cancellationToken)
    {
        logger.LogInformation("Trying to login.");
        logger.LogInformation("Navigating to login page: {loginUrl}", LoginUrl);
        await page.GotoAsync(LoginUrl);
        cancellationToken.ThrowIfCancellationRequested();

        // Wait for the non-scan login form to appear.
        await page.Locator(".user").ClickAsync();

        var loggedIn = false;
        switch (captchaService)
        {
            case ISliderCaptchaService:
            {
                for (var i = 0; i < _maxAttempts; i++)
                {
                    logger.LogInformation("Attempt {attempt} of {maxAttempts} to login.", i + 1, _maxAttempts);
                    cancellationToken.ThrowIfCancellationRequested();

                    loggedIn = await LoginBySliderAsync(page, cancellationToken);
                    if (loggedIn)
                    {
                        break;
                    }

                    // If login failed, reload the page to reset the state.
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, i)), cancellationToken);
                    await page.ReloadAsync();
                    await page.Locator(".user").ClickAsync();
                }
                break;
            }
            case ISmsCaptchaService:
                loggedIn = await LoginBySmsAsync(page, cancellationToken);
                break;
        }

        return loggedIn;
    }

    private async Task<bool> LoginBySmsAsync(IPage page, CancellationToken cancellationToken)
    {
        logger.LogInformation("Trying to login by sms code.");
        await page.Locator(".code_login").ClickAsync();

        await page.GetByPlaceholder("手机号码").FillAsync(_username);
        await page.Locator(".checked-box.un-checked").Last.ClickAsync();

        cancellationToken.ThrowIfCancellationRequested();

        // Get sms.
        await page.Locator(".send_code a").ClickAsync();
        var smsCaptchaService = captchaService as ISmsCaptchaService;
        var code = await smsCaptchaService!.SolveAsync(cancellationToken);
        logger.LogInformation("Solved captcha with received sms: {code}", code);

        cancellationToken.ThrowIfCancellationRequested();
        await page.GetByPlaceholder("请输入验证码").FillAsync(code);
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "登录" }).ClickAsync();

        await WaitForPageRedirectAsync(page);
        logger.LogInformation("Successfully logged in with sms code: {code}", code);
        return true;
    }

    private async Task<bool> LoginBySliderAsync(IPage page, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_password))
        {
            throw new ArgumentException(nameof(_password));
        }

        logger.LogInformation("Trying to login by slider.");
        await page.Locator(".password_login").ClickAsync();

        await page.GetByPlaceholder("请输入用户名/手机号/邮箱").FillAsync(_username);
        await page.GetByPlaceholder("请输入密码").FillAsync(_password);
        await page.Locator(".checked-box.un-checked").First.ClickAsync();

        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "登录" }).ClickAsync();

        // Wait for the captcha to appear and solve it.
        await page.Locator(".el-loading-mask.is-fullscreen").WaitForAsync();
        await page.Locator(".el-loading-mask.is-fullscreen").WaitForAsync(
            new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Hidden,
            });
        var captchaCanvas = page.Locator("#slideVerify").Locator("canvas").First;
        var imageBytes = await captchaCanvas.ScreenshotAsync();
        if (imageBytes.Length == 0)
        {
            logger.LogWarning("Failed to find captcha canvas.");
            return false;
        }

        var sliderCaptchaService = captchaService as ISliderCaptchaService;
        var distance = await sliderCaptchaService!.SolveAsync(imageBytes, cancellationToken);
        logger.LogInformation("Solved captcha with sliding distance: {distance}", distance);
        cancellationToken.ThrowIfCancellationRequested();

        var slider = await page
            .Locator(".slide-verify-slider-mask-item")
            .BoundingBoxAsync();
        if (slider == null)
        {
            logger.LogWarning("Failed to find slider bounding box.");
            return false;
        }
        logger.LogInformation("Found slider bounding box: {sliderX} {sliderY}", slider.X, slider.Y);

        // Slide the slider to the right by the distance.
        await page.Mouse.MoveAsync(slider.X + slider.Width / 2, slider.Y + slider.Height / 2);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(slider.X + slider.Width / 2 + distance, slider.Y + slider.Height / 2, new MouseMoveOptions { Steps = 5 });
        await page.Mouse.UpAsync();

        cancellationToken.ThrowIfCancellationRequested();
        if (!await WaitForPageRedirectAsync(page))
        {
            var rk = await page.GetByText("RK001", new PageGetByTextOptions { Exact = false }).CountAsync();
            if (rk > 0)
            {
                logger.LogError("Found RK001 error which indicates unrecoverable network issues.");
                throw new InvalidOperationException("Network error occurred, unable to connect to the website.");
            }

            logger.LogWarning("Failed to login and still on login page.");
            return false;
        }

        logger.LogInformation("Successfully logged in with slider.");
        return true;
    }

    private async Task<bool> WaitForPageRedirectAsync(IPage page)
    {
        try
        {
            await page.WaitForURLAsync(RedirectUrl);
            return true;
        }
        catch (TimeoutException ex)
        {
            logger.LogError(ex, "Failed to wait for page redirect to {redirectUrl}", RedirectUrl);
        }

        return false;
    }

    private static async Task<Dictionary<string, string>> GetUserIdsAsync(IPage page, CancellationToken cancellationToken)
    {
        await page.GotoAsync(DoorNumberUrl);
        await page.Locator(".user-tel").WaitForAsync();

        cancellationToken.ThrowIfCancellationRequested();
        var sections = await page.Locator("section.info-box").AllAsync();
        var userIds = new Dictionary<string, string>();
        foreach (ILocator s in sections)
        {
            var number = await s.Locator("p.tel").GetAttributeAsync("title");
            var name = await s.Locator("span.user").TextContentAsync();
            if (!string.IsNullOrEmpty(number) && !string.IsNullOrEmpty(name))
            {
                userIds.Add(number, name);
            }
        }
        return userIds;
    }

    private async Task<ElectricityData> GetElectricityDataForUserAsync(IPage page, string userId, string userMatch, CancellationToken cancellationToken)
    {
        await page.GotoAsync(BalanceUrl);
        await ChooseCurrentUserAsync(page, userMatch);
        var balance = await GetElectricBalanceAsync(page, cancellationToken);

        await page.GotoAsync(ElectricUsageUrl);
        await ChooseCurrentUserAsync(page, userMatch);
        var (yearlyUsage, yearlyCharge) = await GetYearlyUsageAsync(page, cancellationToken);
        var monthlySummaries = await GetMonthlyUsageAsync(page, cancellationToken);
        var recentDailyUsages = await GetDailyUsageAsync(page, cancellationToken);

        var latestMonthSummary = monthlySummaries.MaxBy(s => s.Month);
        var latestDayRecord = recentDailyUsages.MaxBy(r => r.Date);

        return new ElectricityData(
            userId,
            balance,
            latestDayRecord?.Date,
            latestDayRecord?.Usage,
            yearlyUsage,
            yearlyCharge,
            latestMonthSummary?.Usage,
            latestMonthSummary?.Charge,
            recentDailyUsages,
            monthlySummaries);
    }

    private async Task ChooseCurrentUserAsync(IPage page, string userMatch)
    {
        await page.Locator(".houseNum").GetByRole(AriaRole.Textbox, new LocatorGetByRoleOptions { Name = "请选择" }).ClickAsync();
        await page.Locator(".popper__arrow").WaitForAsync();

        await page
            .Locator("li.el-select-dropdown__item")
            .Filter(new LocatorFilterOptions
            {
                HasText = userMatch,
                Visible = true,
            })
            .ClickAsync();
        logger.LogInformation("Choosing user by match: {userMatch}", userMatch);
    }

    private async Task<decimal> GetElectricBalanceAsync(IPage page, CancellationToken cancellationToken)
    {
        // Yes, it's 'acccount'.
        var balanceText = await page.Locator(".acccount .amt .num").TextContentAsync();
        var amountText = await page.Locator(".acccount .amt.light .amttxt").TextContentAsync();
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(balanceText))
        {
            logger.LogError("Failed to retrieve balance or amount text.");
            throw new InvalidOperationException("Failed to retrieve balance or amount text.");
        }

        var balance = decimal.Parse(balanceText);
        var signedBalance = amountText?.Contains("已结清") ?? true ? balance : -balance;
        logger.LogInformation("Found electric balance: {signedBalance}", signedBalance);

        return signedBalance;
    }

    private async Task<(double Usage, decimal Charge)> GetYearlyUsageAsync(IPage page, CancellationToken cancellationToken)
    {
        await page.Locator("#tab-first").ClickAsync();

        // January is the first month of the year, we need to get last year's data.
        var now = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(+8));
        if (now.Month == 1)
        {
            await page.Locator(".mouthbig").GetByPlaceholder("请选择").ClickAsync();
            await page
                .Locator("li.el-select-dropdown__item")
                .Filter(
                    new LocatorFilterOptions
                    {
                        Visible = true,
                    })
                .GetByText((now.Year - 1).ToString())
                .ClickAsync();
        }

        cancellationToken.ThrowIfCancellationRequested();
        await page.Locator("ul.total").WaitForAsync();

        var yearlyUsageText = await page.Locator(".total li:nth-child(1) span").TextContentAsync();
        var yearlyChargeText = await page.Locator(".total li:nth-child(2) span").TextContentAsync();

        if (!double.TryParse(yearlyUsageText, out var yearlyUsage) || !decimal.TryParse(yearlyChargeText, out var yearlyCharge))
        {
            logger.LogError("Failed to parse yearly usage or charge text: {yearlyUsageText}, {yearlyChargeText}", yearlyUsageText, yearlyChargeText);
            throw new InvalidOperationException("Failed to parse yearly usage or charge text.");
        }

        logger.LogInformation("Found yearly usage: {Usage} - Yearly charge: {Charge}", yearlyUsageText, yearlyChargeText);
        return (Usage: yearlyUsage, Charge: yearlyCharge);
    }

    private async Task<List<MonthlyUsageRecord>> GetMonthlyUsageAsync(IPage page, CancellationToken cancellationToken)
    {
        await page.Locator("#tab-first").ClickAsync();
        cancellationToken.ThrowIfCancellationRequested();

        // January is the first month of the year, we need to get last year's data.
        var now = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(+8));
        if (now.Month == 1)
        {
            await page.Locator(".mouthbig").GetByPlaceholder("请选择").ClickAsync();
            await page
                .Locator("li.el-select-dropdown__item")
                .Filter(
                    new LocatorFilterOptions
                    {
                        Visible = true,
                    })
                .GetByText((now.Year - 1).ToString())
                .ClickAsync();
        }

        cancellationToken.ThrowIfCancellationRequested();
        var monthlyRows = await page.Locator(".mouthbig .main table.el-table__body tbody tr").AllAsync();
        var records = new List<MonthlyUsageRecord>();

        foreach (var row in monthlyRows)
        {
            var cells = await row.Locator("td").AllAsync();
            if (cells.Count < 3)
            {
                // Just skip rows that don't have enough cells.
                logger.LogWarning("Failed to read monthly usage row, not enough cells: {cellsCount}", cells.Count);
                continue;
            }

            var dateText = await cells[0].Locator("span").Last.TextContentAsync();
            var usageText = await cells[1].Locator("span").First.TextContentAsync();
            var chargeText = await cells[2].Locator("span").First.TextContentAsync();
            if (!DateOnly.TryParse(dateText, out var date)
                || !double.TryParse(usageText, out var usage)
                || !decimal.TryParse(chargeText, out var charge))
            {
                // If we cannot parse the text, log a warning and skip.
                var rowText = await row.TextContentAsync();
                logger.LogWarning("Failed to parse monthly usage row: {rowText}", rowText);
                continue;
            }

            var month = date.ToString("yyyy-MM");
            logger.LogInformation("Found monthly usage: {month}, {usage}, {charge}", month, usageText, chargeText);
            records.Add(new MonthlyUsageRecord(month, usage, charge));
            cancellationToken.ThrowIfCancellationRequested();
        }

        return records;
    }

    private async Task<List<DailyUsageRecord>> GetDailyUsageAsync(IPage page, CancellationToken cancellationToken)
    {
        await page.Locator("#tab-second").ClickAsync();
        cancellationToken.ThrowIfCancellationRequested();

        // Choose the last 7 days for daily usage.
        await page.GetByText("近7天").ClickAsync();
        await page.Locator(".about").WaitForAsync();

        cancellationToken.ThrowIfCancellationRequested();
        var dailyRows = await page.Locator(".el-table.about-table table.el-table__body tbody tr").AllAsync();
        var records = new List<DailyUsageRecord>();

        foreach (var row in dailyRows)
        {
            var cells = await row.Locator("td").AllAsync();
            if (cells.Count < 2)
            {
                // Just skip rows that don't have enough cells.
                logger.LogWarning("Failed to read daily usage row, not enough cells: {cellsCount}", cells.Count);
                continue;
            }

            var dateText = await cells[0].TextContentAsync();
            var usageText = await cells[1].TextContentAsync();
            if (!DateOnly.TryParse(dateText, out var date) || !double.TryParse(usageText, out var usage))
            {
                // If we cannot parse the text, log a warning and skip.
                var rowText = await row.TextContentAsync();
                logger.LogWarning("Failed to parse daily usage row: {rowText}", rowText);
                continue;
            }

            logger.LogInformation("Found daily usage: {date}, {usage}", dateText, usageText);
            records.Add(new DailyUsageRecord(date, usage));
            cancellationToken.ThrowIfCancellationRequested();
        }

        return records;
    }
}

public sealed class PlaywrightBrowserFactory : IAsyncDisposable
{
    private readonly AsyncLazy<IBrowser> _browserLazy;
    private IPlaywright? _playwright;

    public PlaywrightBrowserFactory(IHostEnvironment hostEnvironment)
    {
        _browserLazy = new AsyncLazy<IBrowser>(async () =>
        {
            var exitCode = Microsoft.Playwright.Program.Main(["install"]);
            if (exitCode != 0)
            {
                throw new InvalidOperationException($"Playwright installation failed with exit code {exitCode}");
            }

            _playwright = await Playwright.CreateAsync();
            return await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = !hostEnvironment.IsDevelopment(),
                Args = ["--disable-dev-shm-usage"],
            });
        });
    }

    public async Task<IPage> NewPageAsync()
    {
        var browser = await _browserLazy;
        return await browser.NewPageAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_browserLazy.Task.IsCompletedSuccessfully)
        {
            var browser = await _browserLazy;
            await browser.DisposeAsync();
        }

        _playwright?.Dispose();
    }
}
