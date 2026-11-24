using System.Net;
using System.Net.Mail;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Urfair.Api.Data;

namespace Urfair.Api;

/// <summary>
/// E-mails go through an outbox: every mail is a row in Mails. With a mail server set up (Mail:Host) and outside demo mode,
/// new mails are marked for sending and <see cref="MailSender"/> sends them in the background; otherwise they are only shown
/// in the app (demo mailbox, or texts the organisers copy). Plain text from the app's address, replies to the person responsible.
/// </summary>
public static class Mailing
{
	public const int MaxAttempts = 5;
	/// <summary>Waiting time after the 1st, 2nd, ... failed attempt.</summary>
	static readonly TimeSpan[] Backoff = [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30), TimeSpan.FromHours(2)];

	static string? configuredBaseUrl;
	static string? seenBaseUrl;

	/// <summary>New mails are sent by the app.</summary>
	public static bool Enabled { get; private set; }

	public static void Configure(IConfiguration cfg, ILogger logger)
	{
		configuredBaseUrl = cfg["App:BaseUrl"]?.TrimEnd('/');
		var host = cfg["Mail:Host"];
		if (string.IsNullOrWhiteSpace(host)) return;
		if (Database.IsDemo(cfg)) { logger.LogInformation("Demo mode: e-mails stay in the demo mailbox, Mail:Host is ignored."); return; }
		if (From(cfg) is null) { logger.LogWarning("Mail:Host is set, but Mail:From (or Mail:User) is no e-mail address: e-mails are not sent."); return; }
		Enabled = true;
	}

	/// <summary>Address for links in mails: App:BaseUrl, otherwise the host of the request (remembered for mails made without one).</summary>
	public static string BaseUrl(HttpRequest http) =>
		configuredBaseUrl ?? (seenBaseUrl = $"{(http.Host.Host is "localhost" or "127.0.0.1" ? http.Scheme : "https")}://{http.Host}");

	/// <summary>For mails made without a request (e.g. moving up every minute); null until the app was opened once.</summary>
	public static string? KnownBaseUrl => configuredBaseUrl ?? seenBaseUrl;

	/// <summary>Adds a mail of the vote: replies go to its person responsible; marked for sending if a mail server is set up.</summary>
	public static void Add(AppDbContext db, FairSlotEntity slot, MailEntity mail)
	{
		mail.FairSlotId = slot.Id;
		mail.ReplyTo = slot.OrganiserEmail;
		mail.Outbox = Enabled;
		db.Mails.Add(mail);
	}

	/// <summary>Adds a mail about an organiser account (no vote): access links and password requests, listed on the accounts page.</summary>
	public static void AddAccountMail(AppDbContext db, MailEntity mail, string? replyTo = null)
	{
		mail.FairSlotId = null;
		mail.ReplyTo = replyTo;
		mail.Outbox = Enabled;
		db.Mails.Add(mail);
	}

	/// <summary>manual (copied by hand), pending (also while retrying after an error), sent or failed (gave up).</summary>
	public static string Status(MailEntity m) =>
		!m.Outbox ? "manual" : m.SentAt is not null ? "sent" : m.Attempts >= MaxAttempts ? "failed" : "pending";

	/// <summary>Sends the mails that are due, oldest first; each result is saved at once so nothing goes out twice.</summary>
	public static async Task<int> SendDueAsync(AppDbContext db, IConfiguration cfg, CancellationToken ct)
	{
		var now = DateTime.UtcNow;
		var due = await db.Mails
			.Where(m => m.Outbox && m.SentAt == null && m.Attempts < MaxAttempts && (m.NextAttemptAt == null || m.NextAttemptAt <= now))
			.OrderBy(m => m.CreatedAt).Take(50).ToListAsync(ct);
		if (due.Count == 0) return 0;
		using var client = new SmtpClient(cfg["Mail:Host"], cfg.GetValue("Mail:Port", 587))
		{
			// STARTTLS (port 587); implicit TLS (port 465) is not supported by SmtpClient.
			EnableSsl = cfg.GetValue("Mail:Ssl", true),
			Credentials = cfg["Mail:User"] is { Length: > 0 } user ? new NetworkCredential(user, cfg["Mail:Password"]) : null,
			Timeout = 30_000,
		};
		var from = From(cfg)!;
		var sent = 0;
		foreach (var m in due)
		{
			try
			{
				using var message = new MailMessage(from, new MailAddress(m.To))
				{
					Subject = m.Subject, Body = m.Body.ReplaceLineEndings("\r\n"), SubjectEncoding = Encoding.UTF8, BodyEncoding = Encoding.UTF8,
				};
				if (m.ReplyTo is not null) message.ReplyToList.Add(m.ReplyTo);
				await client.SendMailAsync(message, ct);
				(m.SentAt, m.Error, m.NextAttemptAt) = (DateTime.UtcNow, null, null);
				sent++;
			}
			catch (Exception e) when (e is not OperationCanceledException)
			{
				m.Attempts++;
				var error = e.InnerException is { } inner ? $"{e.Message} {inner.Message}" : e.Message;
				m.Error = error.Length > 500 ? error[..500] : error;
				m.NextAttemptAt = DateTime.UtcNow + Backoff[Math.Min(m.Attempts, Backoff.Length) - 1];
			}
			await db.SaveChangesAsync(ct);
		}
		return sent;
	}

	/// <summary>Sends a mail that gave up (or is waiting after an error) again at the next run.</summary>
	public static void Retry(MailEntity m)
	{
		if (!m.Outbox || m.SentAt is not null) return;
		(m.Attempts, m.NextAttemptAt) = (0, null);
	}

	static MailAddress? From(IConfiguration cfg) =>
		MailAddress.TryCreate(cfg["Mail:From"] ?? cfg["Mail:User"], cfg["Mail:FromName"] ?? "Urfair", out var a) ? a : null;
}

/// <summary>Sends the outbox every 20 seconds while sending is set up.</summary>
public sealed class MailSender(IServiceScopeFactory scopes, IConfiguration cfg, ILogger<MailSender> logger) : BackgroundService
{
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		if (!Mailing.Enabled) return;
		using var timer = new PeriodicTimer(TimeSpan.FromSeconds(20));
		while (await timer.WaitForNextTickAsync(stoppingToken))
		{
			try
			{
				using var scope = scopes.CreateScope();
				await Mailing.SendDueAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), cfg, stoppingToken);
			}
			catch (Exception e) when (e is not OperationCanceledException)
			{
				logger.LogError(e, "Sending e-mails failed.");
			}
		}
	}
}
