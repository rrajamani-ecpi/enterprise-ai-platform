using EnterpriseAIPlatform.Application.Identity;
using EnterpriseAIPlatform.Application.Personas;
using EnterpriseAIPlatform.Domain.Identity;
using EnterpriseAIPlatform.Domain.Personas;

namespace EnterpriseAIPlatform.UnitTests.Personas;

/// <summary>
/// Spec 009 US2 / FR-003–FR-005/FR-008 / SC-002/SC-003: the role x ownership x lesson-persona
/// decision matrix produced by <see cref="PersonaAccessEvaluator"/>.
/// </summary>
public class PersonaAccessEvaluatorTests
{
    private const string OwnerKey = "owner-hash";
    private const string CollaboratorKey = "collaborator-hash";
    private const string UnrelatedKey = "unrelated-hash";

    private static PersonaModel Persona(bool isLessonPersona = false) => new()
    {
        Id = "p1",
        OwnerUserId = "owner@contoso.com",
        OwnerPartitionKey = OwnerKey,
        Model = "azure-foundry:gpt-5",
        Name = "Test Persona",
        CollaboratorPartitionKeys = new List<string> { CollaboratorKey },
        IsLessonPersona = isLessonPersona,
    };

    private static UserModel Caller(bool isAdmin = false, bool isEmployee = false, bool isContractor = false, bool isStudent = false) => new()
    {
        Name = "Test Caller",
        Email = "caller@contoso.com",
        Roles = new RoleFlags(isAdmin, isEmployee, isContractor, isStudent),
    };

    [Fact]
    public void Admin_GetsFullAccess_RegardlessOfOwnership()
    {
        var result = PersonaAccessEvaluator.Evaluate(Persona(), Caller(isAdmin: true), UnrelatedKey);

        Assert.Equal(PersonaAccessResult.FullAccess, result);
    }

    [Fact]
    public void Owner_GetsFullAccess()
    {
        var result = PersonaAccessEvaluator.Evaluate(Persona(), Caller(isEmployee: true), OwnerKey);

        Assert.Equal(PersonaAccessResult.FullAccess, result);
    }

    [Fact]
    public void Collaborator_GetsFullAccess()
    {
        var result = PersonaAccessEvaluator.Evaluate(Persona(), Caller(isEmployee: true), CollaboratorKey);

        Assert.Equal(PersonaAccessResult.FullAccess, result);
    }

    [Fact]
    public void Student_GetsReadOnly_OnLessonPersona()
    {
        var result = PersonaAccessEvaluator.Evaluate(Persona(isLessonPersona: true), Caller(isStudent: true), UnrelatedKey);

        Assert.Equal(PersonaAccessResult.ReadOnly, result);
    }

    [Fact]
    public void Student_IsDenied_OnRegularPersona()
    {
        var result = PersonaAccessEvaluator.Evaluate(Persona(), Caller(isStudent: true), UnrelatedKey);

        Assert.Equal(PersonaAccessResult.Denied, result);
    }

    [Fact]
    public void UnrelatedAuthenticatedUser_IsDenied()
    {
        var result = PersonaAccessEvaluator.Evaluate(Persona(), Caller(isEmployee: true), UnrelatedKey);

        Assert.Equal(PersonaAccessResult.Denied, result);
    }

    [Theory]
    [InlineData(PersonaOperation.Edit)]
    [InlineData(PersonaOperation.Delete)]
    public void CanWrite_True_ForOwner_OnRegularPersona(PersonaOperation operation)
    {
        var canWrite = PersonaAccessEvaluator.CanWrite(Persona(), Caller(isEmployee: true), OwnerKey, operation);

        Assert.True(canWrite);
    }

    [Theory]
    [InlineData(PersonaOperation.Edit)]
    [InlineData(PersonaOperation.Delete)]
    public void CanWrite_False_ForCollaborator_OnLessonPersona_EvenThoughFullAccess(PersonaOperation operation)
    {
        // Spec Edge Cases: the lesson-persona write block applies even to a designated collaborator.
        var canWrite = PersonaAccessEvaluator.CanWrite(Persona(isLessonPersona: true), Caller(isEmployee: true), CollaboratorKey, operation);

        Assert.False(canWrite);
    }

    [Theory]
    [InlineData(PersonaOperation.Edit)]
    [InlineData(PersonaOperation.Delete)]
    public void CanWrite_True_ForAdmin_OnLessonPersona(PersonaOperation operation)
    {
        var canWrite = PersonaAccessEvaluator.CanWrite(Persona(isLessonPersona: true), Caller(isAdmin: true), UnrelatedKey, operation);

        Assert.True(canWrite);
    }

    [Fact]
    public void CanWrite_False_ForUnrelatedUser_RegardlessOfOperation()
    {
        var canWrite = PersonaAccessEvaluator.CanWrite(Persona(), Caller(isEmployee: true), UnrelatedKey, PersonaOperation.Edit);

        Assert.False(canWrite);
    }

    [Fact]
    public void CanShareGroupTarget_True_ForAdmin()
    {
        Assert.True(PersonaAccessEvaluator.CanShareGroupTarget(Caller(isAdmin: true)));
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void CanShareGroupTarget_False_ForAnyNonAdminRole(bool isEmployee, bool isContractor, bool isStudent)
    {
        Assert.False(PersonaAccessEvaluator.CanShareGroupTarget(Caller(isEmployee: isEmployee, isContractor: isContractor, isStudent: isStudent)));
    }
}
