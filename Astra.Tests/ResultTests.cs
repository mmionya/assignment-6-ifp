using Astra.Core;
using Xunit;

namespace Astra.Tests;

public sealed class ResultTests
{
    [Fact]
    public void PrimitiveSomeValuesRemainPresentForZeroFalseAndEmptyString()
    {
        Option<int> zero = new Option<int>.Some(0);
        Option<bool> falseValue = new Option<bool>.Some(false);
        Option<string> empty = new Option<string>.Some("");

        Assert.Equal(0, Assert.IsType<Option<int>.Some>(zero).Value);
        Assert.False(Assert.IsType<Option<bool>.Some>(falseValue).Value);
        Assert.Equal("", Assert.IsType<Option<string>.Some>(empty).Value);
        Assert.IsType<Option<int>.None>(new Option<int>.None());
    }

    [Fact]
    public void MapPassesTheSuccessfulPayloadAndTransformsItsType()
    {
        var observed = 0;
        var mapped = Result<int>.Success(4).Map(value =>
        {
            observed = value;
            return $"Passengers: {value * 2}";
        });

        Assert.Equal(4, observed);
        Assert.Equal("Passengers: 8", mapped.Value);
    }

    [Fact]
    public void BindPassesThePayloadAndPreservesTheNextResult()
    {
        var nextResult = Result<string>.Success("Quote A");
        var observed = 0;
        var bound = Result<int>.Success(4).Bind(value =>
        {
            observed = value;
            return nextResult;
        });

        Assert.Equal(4, observed);
        Assert.Same(nextResult, bound);
        var rejected = Result<string>.Failure("Quote rejected");
        Assert.Same(rejected, Result<int>.Success(4).Bind(_ => rejected));
    }

    [Fact]
    public void FailurePropagatesUnchangedAndSkipsBothDependentCallbacks()
    {
        var mapCalls = 0;
        var bindCalls = 0;
        var initial = Result<int>.Failure("Booking missing: A");
        var result = initial
            .Map(value => { mapCalls++; return value * 2; })
            .Bind(value => { bindCalls++; return Result<string>.Success(value.ToString()); });

        Assert.False(result.IsSuccess);
        Assert.Equal(initial.Error, result.Error);
        Assert.Throws<InvalidOperationException>(() => result.Value);
        Assert.Equal(0, mapCalls);
        Assert.Equal(0, bindCalls);
    }

    [Fact]
    public void ResultRejectsContradictorySuccessAndUninformativeFailure()
    {
        Assert.Throws<ArgumentNullException>(() => Result<string?>.Success(null));
        Assert.Throws<ArgumentException>(() => Result<int>.Failure(""));
        Assert.Throws<ArgumentException>(() => Result<int>.Failure(" \t "));
        Assert.Equal(0, Result<int>.Success(0).Value);
        Assert.False(Result<bool>.Success(false).Value);
    }

    [Fact]
    public void MatchCallsOnlyTheActiveCaseHandler()
    {
        var successCalls = 0;
        var failureCalls = 0;
        string Success(int value) { successCalls++; return $"Quote {value}"; }
        string Failure(string error) { failureCalls++; return error; }

        Assert.Equal("Quote 7", Result<int>.Success(7).Match(Success, Failure));
        Assert.Equal(1, successCalls);
        Assert.Equal(0, failureCalls);
        Assert.Equal("Cannot quote A", Result<int>.Failure("Cannot quote A").Match(Success, Failure));
        Assert.Equal(1, successCalls);
        Assert.Equal(1, failureCalls);
    }

    [Fact]
    public void WithContextPrefixesFailuresKeepsTheMessageAndLeavesSuccessUntouched()
    {
        var success = Result<int>.Success(7);
        var failure = Result<int>.Failure("Passengers must be between 1 and 6");

        Assert.Same(success, success.WithContext("Input 'agency.json'"));
        var withContext = failure.WithContext("Input 'agency.json'");
        Assert.False(withContext.IsSuccess);
        Assert.Equal("Input 'agency.json': Passengers must be between 1 and 6", withContext.Error);
        Assert.Throws<ArgumentException>(() => failure.WithContext(" "));
    }
}
