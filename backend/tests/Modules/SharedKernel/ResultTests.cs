using Nihasdesk.SharedKernel;

namespace Nihasdesk.Modules.Tests.SharedKernel;

public class ResultTests
{
    private static readonly Error NotFound = Error.NotFound("contact.not_found", "Contact not found.");

    [Fact]
    public void Success_has_value_and_no_error()
    {
        var result = Result.Success(42);

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
        Assert.Equal(Error.None, result.Error);
    }

    [Fact]
    public void Failure_has_error_and_throws_on_value()
    {
        Result<int> result = NotFound;

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Throws<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void Failure_requires_an_error()
    {
        Assert.Throws<ArgumentException>(() => Result.Failure(Error.None));
    }
}
