using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Application.Modules.IdentityAccess.Domain;
using LandErp.Infrastructure.Modules.Organization;
using LandErp.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace LandErp.Infrastructure.Modules.IdentityAccess;

public sealed class AccountActivation(LandErpDbContext db, UserManager<LandErpUser> users)
{
    public async Task ActivateAsync(Guid invitationId, string token, string password, CancellationToken cancellationToken)
    {
        if (token.Length != 64 || !token.All(char.IsAsciiHexDigit))
        {
            throw new ArgumentException("Приглашение недействительно или истекло.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        EmployeeInvitation invitation = await db.EmployeeInvitations.SingleOrDefaultAsync(item => item.Id == invitationId, cancellationToken)
            ?? throw new ArgumentException("Приглашение недействительно или истекло.");
        if (invitation.AcceptedAt != null || invitation.ExpiresAt <= DateTimeOffset.UtcNow
            || !CryptographicOperations.FixedTimeEquals(Convert.FromHexString(invitation.TokenHash),
                Convert.FromHexString(OrganizationWorkspace.HashToken(token))))
        {
            throw new ArgumentException("Приглашение недействительно или истекло.");
        }

        var employee = await db.Employees.SingleAsync(item => item.Id == invitation.EmployeeId, cancellationToken);
        LandErpUser user = await users.FindByIdAsync(employee.UserId.ToString()) ?? throw new AccessDeniedException();
        OrganizationWorkspace.EnsureIdentity(await users.AddPasswordAsync(user, password));
        user.EmailConfirmed = true;
        OrganizationWorkspace.EnsureIdentity(await users.UpdateAsync(user));
        invitation.AcceptedAt = DateTimeOffset.UtcNow;
        employee.Active = true;
        OrganizationWorkspace.AddAudit(db, new(employee.Id, employee.OrganizationId, null, null, AccessScope.Own),
            new(user.Id, false), "EmployeeActivated", "Employee", employee.Id, new { Active = true }, "account-activation");
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
