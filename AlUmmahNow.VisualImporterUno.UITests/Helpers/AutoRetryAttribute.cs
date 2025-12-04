using NUnit.Framework;
using NUnit.Framework.Interfaces;
using NUnit.Framework.Internal;
using NUnit.Framework.Internal.Commands;

namespace AlUmmahNow.VisualImporterUno.UITests.Helpers;

/// <summary>
/// Attribute that automatically retries a test if it fails.
/// Useful for UI tests that may be flaky due to timing issues.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public class AutoRetryAttribute : NUnitAttribute, IRepeatTest
{
    private readonly int _tryCount;

    /// <summary>
    /// Initializes a new instance of the AutoRetryAttribute.
    /// </summary>
    /// <param name="tryCount">Number of times to try the test (default: 3)</param>
    public AutoRetryAttribute(int tryCount = 3)
    {
        _tryCount = tryCount;
    }

    /// <summary>
    /// Wraps a command to implement retry logic.
    /// </summary>
    public TestCommand Wrap(TestCommand command)
    {
        return new RetryCommand(command, _tryCount);
    }

    /// <summary>
    /// Command that retries a test on failure.
    /// </summary>
    private class RetryCommand : DelegatingTestCommand
    {
        private readonly int _tryCount;

        public RetryCommand(TestCommand innerCommand, int tryCount)
            : base(innerCommand)
        {
            _tryCount = tryCount;
        }

        public override TestResult Execute(TestExecutionContext context)
        {
            int count = _tryCount;

            while (count-- > 0)
            {
                try
                {
                    context.CurrentResult = innerCommand.Execute(context);
                }
                catch (Exception ex)
                {
                    if (count > 0)
                    {
                        continue;
                    }

                    throw;
                }

                if (context.CurrentResult.ResultState != ResultState.Failure &&
                    context.CurrentResult.ResultState != ResultState.Error)
                {
                    break;
                }

                if (count > 0)
                {
                    // Reset the result for the next iteration
                    context.CurrentResult = context.CurrentTest.MakeTestResult();
                }
            }

            return context.CurrentResult;
        }
    }
}
