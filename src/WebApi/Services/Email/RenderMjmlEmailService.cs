using Mjml.Net;
using Resend;

namespace WebApi.Services.Email;

/// <summary>
/// Service for sending emails using <a href="https://resend.com/about">Resend</a> as an email sending problem, and
/// <a href="https://mjml.io/">MJML</a> templates for the email contents.
/// </summary>
public class RenderMjmlEmailService(IResend resendClient, string domain)
{
    private string FromAddress => $"noreply@{domain}";
    
    public async Task SendPasswordResetEmailAsync(string toEmail, string resetLink, CancellationToken cancellationToken = default)
    {
      var emailMjmlTemplate = MjmlTemplates.SendPasswordResetEmailTemplate(resetLink);
      var emailHtmlBody = await new MjmlRenderer().RenderAsync(emailMjmlTemplate, options: null, cancellationToken);
      await SendHtmlEmailAsync(toEmail, "Password Rest Request", emailHtmlBody.Html, cancellationToken);
    }

    public async Task SendHtmlEmailAsync(
        string toEmail, 
        string subject, 
        string htmlBody,  // assumed to be email HTML, not MJML
        CancellationToken cancellationToken = default)
    {
        var resp = await resendClient.EmailSendAsync( new EmailMessage()
        {
            From = FromAddress,
            To = toEmail,
            Subject = subject,
            HtmlBody = htmlBody 
        }, cancellationToken);
        
        if (resp.Exception is not null)
            throw resp.Exception;
    }
}

static class MjmlTemplates
{
    public static string SendPasswordResetEmailTemplate(string resetLink) =>
$"""
<mjml>
  <mj-head>
    <mj-attributes>
      <mj-all font-family="Arial, sans-serif" />

      <mj-text color="#202124" line-height="1.6" />
    </mj-attributes>
  </mj-head>
  <mj-body background-color="#ffffff">
    <mj-section background-color="#ffffff" padding="60px 20px">
      <mj-column border="2px solid #f0f0f0" border-radius="0.25rem" padding="2rem 0">

        <mj-text align="center" font-size="24px" font-weight="400" padding-bottom="6px">
          Reset your password
        </mj-text>

        <mj-text align="left" font-size="14px" color="#202124" padding-bottom="32px">
          Hello,<br/><br/>
          We received a request to reset your password. Click on the following button to reset your password. If you <b>did not</b> request a password reset, ignore this message and let us know.
        </mj-text>

        <mj-button background-color="#1a73e8" color="#ffffff" border-radius="4px" font-size="14px" font-weight="500" padding="12px 24px" href="{resetLink}">
          Reset password
        </mj-button>

        <mj-text align="center" font-size="12px" color="#5f6368" padding-top="12px">
          If the button above does not appear, please copy and paste this link into your browser's address bar:<br />
          <a href="{resetLink}" style="color: #1a73e8;">
            {resetLink}
          </a>
        </mj-text>

      </mj-column>
    </mj-section>

  </mj-body>
</mjml>
""";
}