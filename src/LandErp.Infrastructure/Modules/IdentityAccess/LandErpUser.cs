using Microsoft.AspNetCore.Identity;

namespace LandErp.Infrastructure.Modules.IdentityAccess;

public sealed class LandErpUser : IdentityUser<Guid>
{
    public bool MustChangePassword { get; set; }
}
