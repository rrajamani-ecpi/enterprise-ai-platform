using System.Runtime.CompilerServices;

// Grants access to internal types — currently just IPersonaRawAccessor (spec 009 FR-010,
// research.md D5): Infrastructure implements it, the test projects exercise it directly. A future
// spec 011 (A2A invocation) assembly should be added here when it exists.
[assembly: InternalsVisibleTo("EnterpriseAIPlatform.Infrastructure")]
[assembly: InternalsVisibleTo("EnterpriseAIPlatform.UnitTests")]
[assembly: InternalsVisibleTo("EnterpriseAIPlatform.IntegrationTests")]
[assembly: InternalsVisibleTo("EnterpriseAIPlatform.ArchitectureTests")]
