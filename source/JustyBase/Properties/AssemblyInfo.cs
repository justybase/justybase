using System.Runtime.CompilerServices;

// Allow the test project to access internal members of JustyBase for white-box unit testing.
[assembly: InternalsVisibleTo("JustyBase.Tests")]
[assembly: InternalsVisibleTo("JustyBase.HeadlessTests")]
