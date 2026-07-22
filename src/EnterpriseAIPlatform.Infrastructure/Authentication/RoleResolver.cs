using EnterpriseAIPlatform.Application.Authorization;
using EnterpriseAIPlatform.Domain.Identity;
using Microsoft.Extensions.Options;

namespace EnterpriseAIPlatform.Infrastructure.Authentication;

/// <summary>
/// Single implementation of the group-GUID -> role-flag mapping (spec 002 FR-004).
/// Flags are independent (a user in multiple groups gets multiple flags).
/// </summary>
public sealed class RoleResolver : IRoleResolver
{
    private readonly IReadOnlyDictionary<Guid, RoleName> _map;

    public RoleResolver(IOptions<RoleDerivationMappingOptions> options)
    {
        var map = new Dictionary<Guid, RoleName>();
        foreach (var mapping in options.Value.Mappings)
        {
            if (!map.TryAdd(mapping.GroupId, mapping.Role))
            {
                throw new InvalidOperationException(
                    $"Duplicate role mapping configured for group '{mapping.GroupId}'.");
            }
        }

        _map = map;
    }

    public RoleFlags DeriveFrom(IReadOnlyCollection<Guid> entraGroupIds)
    {
        bool isAdmin = false, isEmployee = false, isContractor = false, isStudent = false;

        foreach (var id in entraGroupIds)
        {
            if (!_map.TryGetValue(id, out var role))
            {
                continue;
            }

            switch (role)
            {
                case RoleName.Admin:
                    isAdmin = true;
                    break;
                case RoleName.Employee:
                    isEmployee = true;
                    break;
                case RoleName.Contractor:
                    isContractor = true;
                    break;
                case RoleName.Student:
                    isStudent = true;
                    break;
            }
        }

        return new RoleFlags(isAdmin, isEmployee, isContractor, isStudent);
    }
}
