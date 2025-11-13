using Microsoft.Exchange.WebServices.Data;
using Microsoft.Extensions.Logging;
using ExchangeCalendarSync.Models;

namespace ExchangeCalendarSync.Services;

public class ExchangeOnPremiseService
{
    private readonly ILogger<ExchangeOnPremiseService> _logger;
    private readonly ExchangeOnPremiseSettings _settings;
    private ExchangeService? _service;

    public ExchangeOnPremiseService(
        ILogger<ExchangeOnPremiseService> logger,
        ExchangeOnPremiseSettings settings)
    {
        _logger = logger;
        _settings = settings;
    }

    public void Initialize()
    {
        try
        {
            _service = new ExchangeService(ExchangeVersion.Exchange2013_SP1)
            {
                Credentials = new WebCredentials(_settings.Username, _settings.Password, _settings.Domain),
                Url = new Uri(_settings.ServerUrl)
            };

            // Accept all SSL certificates (for dev/test - consider proper cert validation in production)
            System.Net.ServicePointManager.ServerCertificateValidationCallback =
                (sender, certificate, chain, sslPolicyErrors) => true;

            _logger.LogInformation("Exchange On-Premise service initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Exchange On-Premise service");
            throw;
        }
    }

    public async Task<List<CalendarItemSync>> GetCalendarItemsAsync(string mailboxEmail, DateTime startDate, DateTime endDate)
    {
        if (_service == null)
        {
            throw new InvalidOperationException("Service not initialized. Call Initialize() first.");
        }

        var items = new List<CalendarItemSync>();

        try
        {
            _logger.LogInformation("Fetching calendar items for {Mailbox} from {Start} to {End}",
                mailboxEmail, startDate, endDate);

            // Impersonate the mailbox
            _service.ImpersonatedUserId = new ImpersonatedUserId(ConnectingIdType.SmtpAddress, mailboxEmail);

            var calendar = await Folder.Bind(_service, WellKnownFolderName.Calendar);

            var calendarView = new CalendarView(startDate, endDate)
            {
                PropertySet = new PropertySet(
                    ItemSchema.Id,
                    ItemSchema.Subject,
                    ItemSchema.Body,
                    ItemSchema.LastModifiedTime,
                    AppointmentSchema.Start,
                    AppointmentSchema.End,
                    AppointmentSchema.Location,
                    AppointmentSchema.IsAllDayEvent,
                    AppointmentSchema.RequiredAttendees,
                    AppointmentSchema.OptionalAttendees,
                    AppointmentSchema.Organizer,
                    AppointmentSchema.Categories,
                    AppointmentSchema.IsRecurring,
                    AppointmentSchema.Recurrence
                )
            };

            var appointments = await _service.FindAppointments(WellKnownFolderName.Calendar, calendarView);

            foreach (var appointment in appointments.Items)
            {
                try
                {
                    var item = new CalendarItemSync
                    {
                        Id = appointment.Id.UniqueId,
                        Subject = appointment.Subject ?? string.Empty,
                        Body = appointment.Body?.Text,
                        Start = appointment.Start,
                        End = appointment.End,
                        Location = appointment.Location ?? string.Empty,
                        IsAllDay = appointment.IsAllDayEvent,
                        RequiredAttendees = appointment.RequiredAttendees.Select(a => a.Address).ToList(),
                        OptionalAttendees = appointment.OptionalAttendees.Select(a => a.Address).ToList(),
                        Organizer = appointment.Organizer?.Address,
                        Categories = appointment.Categories != null && appointment.Categories.Any()
                            ? string.Join(", ", appointment.Categories)
                            : null,
                        IsRecurring = appointment.IsRecurring,
                        RecurrencePattern = appointment.Recurrence?.ToString(),
                        LastModified = appointment.LastModifiedTime,
                        SourceMailbox = mailboxEmail
                    };

                    items.Add(item);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to process calendar item: {Subject}", appointment.Subject);
                }
            }

            _logger.LogInformation("Retrieved {Count} calendar items for {Mailbox}", items.Count, mailboxEmail);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch calendar items for {Mailbox}", mailboxEmail);
            throw;
        }

        return items;
    }
}
