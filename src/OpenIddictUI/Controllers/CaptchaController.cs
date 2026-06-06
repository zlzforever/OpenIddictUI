using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Options;
using OpenIddictUI.Controllers.Input;
using OpenIddictUI.Options;
using SkiaSharp;

namespace OpenIddictUI.Controllers;

[Route("api/v1.0/captcha")]
public class CaptchaController(
    IOptions<OpenIddictUIOptions> options,
    HybridCache hybridCache,
    ILogger<CaptchaController> logger) : Controller
{
    private static readonly SKColor[] Colors =
        [SKColors.Black, SKColors.DarkBlue, SKColors.DarkRed, SKColors.DarkGreen, SKColors.DarkOrange];

    private const int W = 120, H = 38;

    // ---- 图形验证码 ----

    [HttpGet("image")]
    public IActionResult Generate()
    {
        var code = GenerateCode(options.Value.GetVerifyCodeLength());
        var captchaId = Guid.CreateVersion7().ToString("N");
        var cacheKey = string.Format(Util.CaptchaImageKey, captchaId);
        Response.Cookies.Append(Util.CaptchaId, captchaId, new CookieOptions
        {
            MaxAge = TimeSpan.FromMinutes(6)
        });
        hybridCache.SetAsync(cacheKey, code, new HybridCacheEntryOptions
        {
            Expiration = TimeSpan.FromMinutes(3),
            LocalCacheExpiration = TimeSpan.FromMinutes(3)
        });

        logger.LogDebug("[CAPTCHA] {CaptchaId} {CaptchaCode}", captchaId, code);

        using var surface = SKSurface.Create(new SKImageInfo(W, H));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);
        var rng = new Random();

        for (var i = 0; i < 15; i++)
        {
            using var p = new SKPaint();
            p.Color = new SKColor((byte)rng.Next(200), (byte)rng.Next(200), (byte)rng.Next(200));
            p.StrokeWidth = 1;
            p.IsAntialias = true;
            canvas.DrawLine(rng.Next(W), rng.Next(H), rng.Next(W), rng.Next(H), p);
        }

        using var font = new SKFont(SKTypeface.Default, 20);
        for (var i = 0; i < code.Length; i++)
        {
            using var p = new SKPaint();
            p.Color = Colors[rng.Next(Colors.Length)];
            p.IsAntialias = true;
            canvas.DrawText(code[i].ToString(), 15 + i * 25 + rng.Next(-3, 3), 28 + rng.Next(-3, 3), SKTextAlign.Left,
                font, p);
        }

        using var img = surface.Snapshot();
        using var data = img.Encode(SKEncodedImageFormat.Jpeg, 80);
        return File(data.ToArray(), "image/jpeg");
    }

    // ---- 滑块验证码 ----

    private const int SliderTolerance = 8;
    private const int TrackW = 340;
    private const int TrackH = 42;
    private const int NotchW = 30;
    private const int MinX = 50;
    private const int MaxX = 290;

    /// <summary>生成带圆形虚线缺口的滑块底图</summary>
    [HttpGet("slider")]
    public async Task<IActionResult> Slider()
    {
        var id = Guid.CreateVersion7().ToString("N");
        var notchX = RandomNumberGenerator.GetInt32(MinX, MaxX + 1);
        var imageBytes = GenerateSliderImage(notchX);

        await hybridCache.SetAsync(string.Format(Util.CaptchaSliderKey, id), notchX,
            new HybridCacheEntryOptions { Expiration = TimeSpan.FromMinutes(3) });

        Response.Cookies.Append("SliderCaptchaId", id, new CookieOptions
        {
            MaxAge = TimeSpan.FromMinutes(3),
            SameSite = SameSiteMode.Lax
        });
        return File(imageBytes, "image/jpeg");
    }

    /// <summary>校验滑块位置</summary>
    [HttpPost("slider/verify")]
    public async Task<IActionResult> SliderVerify([FromBody] SliderVerifyInput input)
    {
        var captchaId = Request.Cookies["SliderCaptchaId"] ?? string.Empty;
        if (string.IsNullOrEmpty(captchaId))
        {
            return Ok(Errors.InvalidParams);
        }

        var key = string.Format(Util.CaptchaSliderKey, captchaId);
        var stored = await hybridCache.GetOrCreateAsync(
            key,
            _ => new ValueTask<int?>((int?)null),
            new HybridCacheEntryOptions { Flags = HybridCacheEntryFlags.DisableLocalCacheWrite });

        if (stored == null)
        {
            return Ok(Errors.SliderCaptchaExpiredResult);
        }

        await hybridCache.RemoveAsync(key);

        var diff = Math.Abs(stored.Value - input.Position);
        var passed = diff <= SliderTolerance;
        if (passed)
        {
            // 标记已验证 → /account/sendCode 会校验此标记
            await hybridCache.SetAsync(string.Format(Util.CaptchaSliderVerified, captchaId), true,
                new HybridCacheEntryOptions { Expiration = TimeSpan.FromMinutes(2) });
        }

        return Ok(passed
            ? ApiResult.Ok()
            : Errors.SliderCaptchaFailedResult);
    }

    /// <summary>
    /// 绘制滑块底图：渐变背景 + 随机噪线 + 虚线圆型缺口
    /// 缺口位置不返回数值，编码在图片中；脚本需逐像素分析才可定位
    /// </summary>
    private static byte[] GenerateSliderImage(int notchX)
    {
        using var surface = SKSurface.Create(new SKImageInfo(TrackW, TrackH));
        var canvas = surface.Canvas;
        var rng = new Random();

        var r1 = (byte)rng.Next(80, 180);
        var g1 = (byte)rng.Next(80, 180);
        var b1 = (byte)rng.Next(80, 180);
        var r2 = (byte)rng.Next(180, 250);
        var g2 = (byte)rng.Next(180, 250);
        var b2 = (byte)rng.Next(180, 250);

        // 渐变背景
        using var bgPaint = new SKPaint();
        bgPaint.IsAntialias = true;
        for (var x = 0; x < TrackW; x++)
        {
            var t = x / (float)TrackW;
            bgPaint.Color = new SKColor(
                (byte)(r1 + (r2 - r1) * t),
                (byte)(g1 + (g2 - g1) * t),
                (byte)(b1 + (b2 - b1) * t));
            canvas.DrawLine(x, 0, x, TrackH, bgPaint);
        }

        // 随机噪线
        using var noisePaint = new SKPaint();
        noisePaint.IsAntialias = true;
        noisePaint.StrokeWidth = 1;
        for (var i = 0; i < 25; i++)
        {
            noisePaint.Color = new SKColor(
                (byte)rng.Next(256), (byte)rng.Next(256), (byte)rng.Next(256), 80);
            canvas.DrawLine(rng.Next(TrackW), rng.Next(TrackH),
                rng.Next(TrackW), rng.Next(TrackH), noisePaint);
        }

        // 虚线圆型缺口
        var tN = notchX / (float)TrackW;
        using var notchPaint = new SKPaint();
        notchPaint.Style = SKPaintStyle.Stroke;
        notchPaint.StrokeWidth = 2;
        notchPaint.IsAntialias = true;
        notchPaint.PathEffect = SKPathEffect.CreateDash([4f, 4f], 0);
        notchPaint.Color = new SKColor(
            (byte)(255 - (r1 + (r2 - r1) * tN)),
            (byte)(255 - (g1 + (g2 - g1) * tN)),
            (byte)(255 - (b1 + (b2 - b1) * tN)));
        canvas.DrawCircle(notchX, TrackH / 2f, NotchW / 2f, notchPaint);

        // 边框
        using var borderPaint = new SKPaint();
        borderPaint.Style = SKPaintStyle.Stroke;
        borderPaint.Color = SKColors.Gray;
        borderPaint.StrokeWidth = 1;
        canvas.DrawRect(0, 0, TrackW - 1, TrackH - 1, borderPaint);

        using var img = surface.Snapshot();
        using var data = img.Encode(SKEncodedImageFormat.Jpeg, 80);
        return data.ToArray();
    }

    private static string GenerateCode(int len)
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var result = new char[len];

        for (var i = 0; i < len; i++)
        {
            var index = RandomNumberGenerator.GetInt32(chars.Length);
            result[i] = chars[index];
        }

        return new string(result);
    }
}