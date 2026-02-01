using Microsoft.EntityFrameworkCore;
using WebApi.Data;
using WebApi.Models;

namespace WebApi.Repositories;

public class AppointmentRepository(AppDbContext context)
{
    public async Task<List<Appointment>> GetAppointmentsAsync(Guid userId, bool includeCompleted = false)
    {
        var query = context.Appointments
            .Where(a => a.UserId == userId);

        if (!includeCompleted)
        {
            query = query.Where(a => a.Status != "Completed" && a.Status != "Cancelled");
        }

        return await query
            .OrderBy(a => a.DateTime)
            .ToListAsync();
    }

    public async Task<Appointment> BookAsync(Guid userId, string? clinicName, DateTime? dateTime, string? reason, string? urgency = null)
    {
        // Ensure DateTime is UTC for PostgreSQL compatibility
        DateTime? utcDateTime = null;
        if (dateTime.HasValue)
        {
            var dt = dateTime.Value;
            if (dt.Kind == DateTimeKind.Unspecified)
            {
                // Treat Unspecified as UTC (common when AI provides dates)
                utcDateTime = DateTime.SpecifyKind(dt, DateTimeKind.Utc);
            }
            else if (dt.Kind == DateTimeKind.Local)
            {
                // Convert Local to UTC
                utcDateTime = dt.ToUniversalTime();
            }
            else
            {
                // Already UTC
                utcDateTime = dt;
            }
        }

        var appointment = new Appointment
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ClinicName = clinicName,
            DateTime = utcDateTime,
            Reason = reason,
            Status = "Pending",
            Urgency = urgency,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        context.Appointments.Add(appointment);
        await context.SaveChangesAsync();
        return appointment;
    }

    public async Task<bool> CancelAsync(Guid appointmentId)
    {
        var appointment = await context.Appointments
            .FirstOrDefaultAsync(a => a.Id == appointmentId);

        if (appointment == null)
        {
            return false;
        }

        appointment.Status = "Cancelled";
        appointment.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync();
        return true;
    }
}
