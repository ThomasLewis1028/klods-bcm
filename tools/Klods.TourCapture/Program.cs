using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Playwright;

// ── Config ──────────────────────────────────────────────────────────────────
const string webUrl = "http://localhost:5100";
const string apiUrl = "http://localhost:5101";
const string demoUser = "demo", demoPass = "demo";

var tourDir = Path.GetFullPath("../../Klods.Web/wwwroot/tour");
var workDir = Path.GetFullPath("capture-work");
Directory.CreateDirectory(tourDir);
Directory.CreateDirectory(workDir);
var viewport = new ViewportSize { Width = 1280, Height = 800 };
var vsize = new RecordVideoSize { Width = 1280, Height = 800 };

// Playwright drives the real mouse but never paints a cursor, so clicks look like they happen by magic.
// This overlay dot follows pointer events and shrinks on press, making each click legible in the clips.
const string cursorScript = """
(() => {
  const install = () => {
    if (document.getElementById('__pw_cursor')) return;
    const c = document.createElement('div');
    c.id = '__pw_cursor';
    c.style.cssText = 'position:fixed;left:-100px;top:-100px;width:22px;height:22px;border-radius:50%;' +
      'background:rgba(21,101,192,.85);border:2px solid #fff;box-shadow:0 2px 6px rgba(0,0,0,.45);' +
      'z-index:2147483647;pointer-events:none;transform:translate(-50%,-50%);transition:width .08s,height .08s;';
    document.documentElement.appendChild(c);
    const move = e => { c.style.left = e.clientX + 'px'; c.style.top = e.clientY + 'px'; };
    addEventListener('mousemove', move, true);
    addEventListener('pointermove', move, true);
    addEventListener('mousedown', () => { c.style.width = '13px'; c.style.height = '13px'; }, true);
    addEventListener('mouseup', () => { c.style.width = '22px'; c.style.height = '22px'; }, true);
  };
  if (document.readyState === 'loading') addEventListener('DOMContentLoaded', install); else install();
})();
""";

Console.WriteLine("Ensuring the matching Chromium is installed…");
if (Microsoft.Playwright.Program.Main(["install", "chromium"]) is var code and not 0) return code;

using var pw = await Playwright.CreateAsync();
await using var browser = await pw.Chromium.LaunchAsync(new() { Headless = true });

// API client used only for deterministic cleanup of anything the add-set clip adds.
using var http = new HttpClient { BaseAddress = new Uri(apiUrl) };
var loginResp = await http.PostAsJsonAsync("/api/auth/login", new { username = demoUser, password = demoPass });
var token = (await loginResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
http.DefaultRequestHeaders.Authorization = new("Bearer", token);

Console.WriteLine("Logging in + preparing dark/light states…");
var (darkState, lightState) = await SetupStatesAsync();

var ownedBefore = await OwnedKeysAsync();

// Read-only flows first (pristine demo data), the data-mutating add-set last.
// welcome + completeness are stills (a looping video wipes/reloads awkwardly on those static screens).
Console.WriteLine("Capturing clips…");
await CaptureShotBothAsync("welcome", Welcome);
await CaptureBothAsync("global-vs-personal", GlobalVsPersonal);
await CaptureShotBothAsync("completeness", Completeness);
await CaptureBothAsync("bom", Bom);
await CaptureBothAsync("minifigs", Minifigs);
await CaptureBothAsync("personalize", Personalize);
await CaptureBothAsync("add-set", AddSet);

// Cleanup: remove any copies the add-set clip created, restoring the demo account exactly.
foreach (var key in (await OwnedKeysAsync()).Except(ownedBefore))
{
    var (setId, idx) = (key[..key.LastIndexOf('#')], key[(key.LastIndexOf('#') + 1)..]);
    await http.DeleteAsync($"/api/sets/owned/{Uri.EscapeDataString(setId)}/{idx}");
    Console.WriteLine($"  cleaned up added copy {key}");
}

Console.WriteLine("Done. Clips in " + tourDir);
return 0;

// ── Flows ───────────────────────────────────────────────────────────────────
// Welcome + Completeness are captured as stills (CaptureShot*), so these just settle the page.
async Task Welcome(IPage p)
{
    await p.GotoAsync(webUrl, new() { WaitUntil = WaitUntilState.Load });
    await Settle(p);
    await p.WaitForTimeoutAsync(600);
}

async Task GlobalVsPersonal(IPage p)
{
    await p.GotoAsync($"{webUrl}/sets", new() { WaitUntil = WaitUntilState.Load });
    await Settle(p);
    await p.WaitForTimeoutAsync(2500);
    await p.GotoAsync($"{webUrl}/my/sets", new() { WaitUntil = WaitUntilState.Load });
    await Settle(p);
    await p.WaitForTimeoutAsync(2500);
}

async Task Completeness(IPage p)
{
    await p.GotoAsync($"{webUrl}/my/sets", new() { WaitUntil = WaitUntilState.Load });
    await Settle(p);
    await p.WaitForTimeoutAsync(600);
}

async Task Bom(IPage p)
{
    await p.GotoAsync($"{webUrl}/my/sets", new() { WaitUntil = WaitUntilState.Load });
    await Settle(p);
    await p.WaitForTimeoutAsync(1200);
    await ClickWithCursor(p, p.Locator(".catalog-card").First);
    await p.WaitForTimeoutAsync(2200);   // let the set popup settle so the jump reads clearly
    await ClickWithCursor(p, p.GetByRole(AriaRole.Button, new() { Name = "Bill of Materials" }));
    await p.WaitForTimeoutAsync(3800);
}

async Task Minifigs(IPage p)
{
    await p.GotoAsync($"{webUrl}/my/minifigs", new() { WaitUntil = WaitUntilState.Load });
    await Settle(p);
    await p.WaitForTimeoutAsync(1000);
    await ClickWithCursor(p, p.Locator(".catalog-card").First);
    await p.WaitForTimeoutAsync(3500);
}

async Task Personalize(IPage p)
{
    // Show moving from a normal page to Profile, then land and dwell on the settings.
    await p.GotoAsync($"{webUrl}/my/sets", new() { WaitUntil = WaitUntilState.Load });
    await Settle(p);
    await p.WaitForTimeoutAsync(1500);
    await p.GotoAsync($"{webUrl}/profile", new() { WaitUntil = WaitUntilState.Load });
    await Settle(p);
    await p.WaitForTimeoutAsync(1200);
    await p.Mouse.WheelAsync(0, 350);
    await p.WaitForTimeoutAsync(3200);
}

async Task AddSet(IPage p)
{
    await p.GotoAsync($"{webUrl}/sets", new() { WaitUntil = WaitUntilState.Load });
    await Settle(p);
    await p.Locator("input[placeholder*='name']").First.FillAsync("Cafe");
    await p.WaitForTimeoutAsync(2000);
    await ClickWithCursor(p, p.Locator(".catalog-card").First);
    await p.WaitForTimeoutAsync(2500);   // dwell on the set popup before adding
    await ClickWithCursor(p, p.GetByRole(AriaRole.Button, new() { Name = "Add to my collection" }));
    await p.WaitForTimeoutAsync(1800);   // dwell on the add dialog
    await ClickWithCursor(p, p.GetByRole(AriaRole.Button, new() { Name = "Add to Collection" }));
    await p.WaitForTimeoutAsync(2500);
}

// ── Infrastructure ──────────────────────────────────────────────────────────
async Task<(string dark, string light)> SetupStatesAsync()
{
    var ctx = await browser.NewContextAsync(new() { ViewportSize = viewport });
    var page = await ctx.NewPageAsync();
    await page.GotoAsync(webUrl, new() { WaitUntil = WaitUntilState.Load });

    await page.GetByRole(AriaRole.Button, new() { Name = "Sign In" }).First.ClickAsync(new() { Timeout = 30_000 });
    await page.GetByLabel("Username").FillAsync(demoUser);
    await page.GetByLabel("Password").FillAsync(demoPass);
    await page.GetByRole(AriaRole.Button, new() { Name = "Login" }).ClickAsync();
    await page.GetByLabel("Username").WaitForAsync(new() { State = WaitForSelectorState.Detached, Timeout = 30_000 });

    try { await page.GetByRole(AriaRole.Button, new() { Name = "Skip" }).ClickAsync(new() { Timeout = 8_000 }); }
    catch { /* tour already seen */ }
    await page.WaitForTimeoutAsync(1_000);

    var dark = Path.Combine(workDir, "state-dark.json");
    await ctx.StorageStateAsync(new() { Path = dark });

    await page.Locator(".mud-appbar button").First.ClickAsync(); // dark-mode toggle → light
    await page.WaitForTimeoutAsync(800);
    var light = Path.Combine(workDir, "state-light.json");
    await ctx.StorageStateAsync(new() { Path = light });

    await ctx.CloseAsync();
    return (dark, light);
}

async Task CaptureBothAsync(string name, Func<IPage, Task> flow)
{
    await CaptureAsync(name, "dark", darkState, flow);
    await CaptureAsync(name, "light", lightState, flow);
}

async Task CaptureShotBothAsync(string name, Func<IPage, Task> flow)
{
    await CaptureShotAsync(name, "dark", darkState, flow);
    await CaptureShotAsync(name, "light", lightState, flow);
}

// A crisp still instead of a clip — for screens where a looping video would just wipe/reload awkwardly.
async Task CaptureShotAsync(string name, string theme, string state, Func<IPage, Task> flow)
{
    var ctx = await browser.NewContextAsync(new() { ViewportSize = viewport, StorageStatePath = state });
    var page = await ctx.NewPageAsync();
    try { await flow(page); }
    catch (Exception ex) { Console.WriteLine($"  !! {name}.{theme} flow error: {ex.Message}"); }

    var outPath = Path.Combine(tourDir, $"{name}.{theme}.png");
    await page.ScreenshotAsync(new() { Path = outPath });
    await ctx.CloseAsync();

    // Drop any stale clip left over from when this step was a video.
    var oldWebm = Path.Combine(tourDir, $"{name}.{theme}.webm");
    if (File.Exists(oldWebm)) File.Delete(oldWebm);
    Console.WriteLine($"  {name}.{theme}.png  ({new FileInfo(outPath).Length / 1024} KB)");
}

// Move the (now visible) cursor to a target and pause before clicking, so the click reads on camera.
async Task ClickWithCursor(IPage p, ILocator loc)
{
    await loc.ScrollIntoViewIfNeededAsync();
    var box = await loc.BoundingBoxAsync();
    if (box is not null)
    {
        await p.Mouse.MoveAsync(box.X + box.Width / 2, box.Y + box.Height / 2, new() { Steps = 24 });
        await p.WaitForTimeoutAsync(450);
    }
    await loc.ClickAsync();
}

async Task CaptureAsync(string name, string theme, string state, Func<IPage, Task> flow)
{
    var dir = Path.Combine(workDir, $"{name}-{theme}");
    if (Directory.Exists(dir)) Directory.Delete(dir, true);
    Directory.CreateDirectory(dir);

    var ctx = await browser.NewContextAsync(new()
    {
        ViewportSize = viewport, StorageStatePath = state,
        RecordVideoDir = dir, RecordVideoSize = vsize,
    });
    await ctx.AddInitScriptAsync(cursorScript);
    var page = await ctx.NewPageAsync();
    try { await flow(page); }
    catch (Exception ex) { Console.WriteLine($"  !! {name}.{theme} flow error: {ex.Message}"); }
    await ctx.CloseAsync();

    var raw = Directory.GetFiles(dir, "*.webm").First();
    var outPath = Path.Combine(tourDir, $"{name}.{theme}.webm");
    Ffmpeg($"-y -i \"{raw}\" -an -c:v libvpx-vp9 -b:v 0 -crf 36 -deadline good -cpu-used 3 -pix_fmt yuv420p \"{outPath}\"");
    Ffmpeg($"-y -sseof -1 -i \"{outPath}\" -vframes 1 \"{Path.Combine(workDir, $"{name}.{theme}.png")}\"");
    Console.WriteLine($"  {name}.{theme}.webm  ({new FileInfo(outPath).Length / 1024} KB)");
}

async Task<HashSet<string>> OwnedKeysAsync()
{
    var arr = await http.GetFromJsonAsync<JsonElement>("/api/sets/my-owned");
    var keys = new HashSet<string>();
    foreach (var s in arr.EnumerateArray())
    {
        var setId = s.GetProperty("setId").GetString();
        foreach (var inst in s.GetProperty("instances").EnumerateArray())
            keys.Add($"{setId}#{inst.GetProperty("setIndex").GetInt32()}");
    }
    return keys;
}

void Ffmpeg(string args)
{
    using var p = Process.Start(new ProcessStartInfo("ffmpeg", args)
    { RedirectStandardError = true, RedirectStandardOutput = true })!;
    p.WaitForExit();
    if (p.ExitCode != 0) throw new Exception("ffmpeg failed:\n" + p.StandardError.ReadToEnd());
}

// Blazor Server holds a SignalR socket open (NetworkIdle never fires); give the circuit a beat.
static async Task Settle(IPage page) => await page.WaitForTimeoutAsync(1_500);
