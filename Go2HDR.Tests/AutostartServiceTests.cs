using Go2HDR.Services;
using Xunit;

namespace Go2HDR.Tests;

public class AutostartServiceTests
{
    [Fact]
    public void TaskXml_ExtractsExecutableRegardlessOfNamespace()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-16"?>
            <Task xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <Actions Context="Author">
                <Exec>
                  <Command>C:\Program Files\Go2HDR\Go2HDR.exe</Command>
                </Exec>
              </Actions>
            </Task>
            """;

        Assert.True(AutostartService.TryGetTaskExecutable(xml, out string? executable));
        Assert.Equal(@"C:\Program Files\Go2HDR\Go2HDR.exe", executable);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not xml")]
    [InlineData("<Task><Actions /></Task>")]
    public void TaskXml_RejectsMissingOrMalformedCommand(string xml)
    {
        Assert.False(AutostartService.TryGetTaskExecutable(xml, out string? executable));
        Assert.Null(executable);
    }
}
