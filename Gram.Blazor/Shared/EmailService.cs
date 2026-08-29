using System;
using System.Net;
using System.Net.Http;
using System.Net.Mail;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Gram.Blazor.Shared;

public interface IEmailService
{
    bool IsConfigured { get; }
    Task<bool> SendPasswordResetEmailAsync(string recipientEmail, string recipientName, string code, string resetUrl);
}

public sealed class EmailService : IEmailService
{
    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IConfiguration config, IHttpClientFactory httpClientFactory, ILogger<EmailService> logger)
    {
        _config = config;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public bool IsConfigured
    {
        get
        {
            var smtpHost = Environment.GetEnvironmentVariable("SMTP_HOST") ?? _config["Auth:Smtp:Host"];
            var resendKey = Environment.GetEnvironmentVariable("RESEND_API_KEY") ?? _config["Auth:ResendApiKey"];
            return !string.IsNullOrWhiteSpace(smtpHost) || !string.IsNullOrWhiteSpace(resendKey);
        }
    }

    public async Task<bool> SendPasswordResetEmailAsync(string recipientEmail, string recipientName, string code, string resetUrl)
    {
        var smtpHost = Environment.GetEnvironmentVariable("SMTP_HOST") ?? _config["Auth:Smtp:Host"];
        var resendKey = Environment.GetEnvironmentVariable("RESEND_API_KEY") ?? _config["Auth:ResendApiKey"];

        var htmlBody = GenerateResetHtml(recipientName, code, resetUrl);

        if (!string.IsNullOrWhiteSpace(resendKey))
        {
            return await SendViaResendAsync(resendKey, recipientEmail, htmlBody);
        }

        if (!string.IsNullOrWhiteSpace(smtpHost))
        {
            return await SendViaSmtpAsync(smtpHost, recipientEmail, recipientName, htmlBody);
        }

        // Local / Self-Hosted Fallback Logging
        LogLocalRecoveryCode(recipientEmail, code, resetUrl);
        return true;
    }

    private async Task<bool> SendViaResendAsync(string apiKey, string recipientEmail, string html)
    {
        try
        {
            var from = Environment.GetEnvironmentVariable("SMTP_FROM") ?? _config["Auth:Smtp:From"] ?? "Gram <noreply@2helix.org>";
            var client = _httpClientFactory.CreateClient();
            var payload = new JsonObject
            {
                ["from"] = from,
                ["to"] = new JsonArray { recipientEmail },
                ["subject"] = "Reset your Gram password",
                ["html"] = html
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails");
            req.Headers.Add("Authorization", $"Bearer {apiKey}");
            req.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");

            var res = await client.SendAsync(req);
            if (res.IsSuccessStatusCode)
            {
                _logger.LogInformation("Password reset email sent via Resend to {Email}", recipientEmail);
                return true;
            }

            var err = await res.Content.ReadAsStringAsync();
            _logger.LogWarning("Resend API error: {Error}", err);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send reset email via Resend to {Email}", recipientEmail);
            return false;
        }
    }

    private async Task<bool> SendViaSmtpAsync(string host, string recipientEmail, string recipientName, string html)
    {
        try
        {
            var portStr = Environment.GetEnvironmentVariable("SMTP_PORT") ?? _config["Auth:Smtp:Port"] ?? "587";
            int.TryParse(portStr, out var port);
            if (port <= 0) port = 587;

            var user = Environment.GetEnvironmentVariable("SMTP_USER") ?? _config["Auth:Smtp:User"];
            var pass = Environment.GetEnvironmentVariable("SMTP_PASS") ?? _config["Auth:Smtp:Pass"];
            var from = Environment.GetEnvironmentVariable("SMTP_FROM") ?? _config["Auth:Smtp:From"] ?? "noreply@gram.local";

            using var client = new SmtpClient(host, port)
            {
                EnableSsl = port == 465 || port == 587,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                UseDefaultCredentials = string.IsNullOrWhiteSpace(user)
            };

            if (!string.IsNullOrWhiteSpace(user) && !string.IsNullOrWhiteSpace(pass))
            {
                client.Credentials = new NetworkCredential(user, pass);
            }

            using var mail = new MailMessage
            {
                From = new MailAddress(from, "Gram"),
                Subject = "Reset your Gram password",
                Body = html,
                IsBodyHtml = true
            };
            mail.To.Add(new MailAddress(recipientEmail, recipientName));

            await client.SendMailAsync(mail);
            _logger.LogInformation("Password reset email sent via SMTP to {Email}", recipientEmail);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send reset email via SMTP to {Email}", recipientEmail);
            return false;
        }
    }

    private void LogLocalRecoveryCode(string email, string code, string resetUrl)
    {
        var msg = $@"
================================================================================
🔑 [Gram Password Recovery] (Local / Self-Hosted Mode)
Recipient: {email}
6-Digit Reset Code: {code}
Reset Link: {resetUrl}
Expires in: 15 minutes
================================================================================";
        Console.WriteLine(msg);
        _logger.LogInformation("Gram Password Reset Code for {Email}: {Code}", email, code);
    }

    private static string GenerateResetHtml(string name, string code, string resetUrl)
    {
        return $@"
<!DOCTYPE html>
<html>
<head>
  <meta charset='utf-8'>
  <style>
    body {{ background: #06020a; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; color: #f3f0fa; margin: 0; padding: 32px 16px; }}
    .card {{ max-width: 480px; margin: 0 auto; background: #0d0617; border: 1px solid #241b36; border-radius: 12px; padding: 32px; box-shadow: 0 8px 32px rgba(0,0,0,0.6); }}
    .logo {{ font-size: 22px; font-weight: 700; color: #c084fc; letter-spacing: -0.02em; margin-bottom: 24px; }}
    h1 {{ font-size: 20px; font-weight: 600; margin: 0 0 12px 0; color: #ffffff; }}
    p {{ font-size: 14px; line-height: 1.6; color: #a89ec2; margin: 0 0 20px 0; }}
    .code-box {{ background: #160724; border: 1px solid #9333ea; border-radius: 8px; padding: 16px; text-align: center; font-size: 32px; font-weight: 700; letter-spacing: 8px; color: #ffffff; margin: 24px 0; font-family: monospace; }}
    .btn {{ display: block; width: 100%; box-sizing: border-box; text-align: center; background: #9333ea; color: #ffffff !important; padding: 12px 20px; border-radius: 6px; text-decoration: none; font-weight: 600; font-size: 14px; margin-top: 20px; }}
    .footer {{ margin-top: 32px; font-size: 12px; color: #6b6082; text-align: center; }}
  </style>
</head>
<body>
  <div class='card'>
    <div class='logo'>Gram</div>
    <h1>Password Recovery Request</h1>
    <p>Hi {WebUtility.HtmlEncode(name)},</p>
    <p>We received a request to reset your Gram password. Use the 6-digit verification code below to complete your password reset:</p>
    <div class='code-box'>{code}</div>
    <a href='{resetUrl}' class='btn'>Reset Password Directly</a>
    <div class='footer'>
      This code expires in 15 minutes.<br>If you didn't request a password reset, you can safely ignore this email.
    </div>
  </div>
</body>
</html>";
    }
}
