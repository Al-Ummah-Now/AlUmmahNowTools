using NUnit.Framework;
using Uno.UITest;
using Uno.UITest.Helpers.Queries;

namespace AlUmmahNow.VisualImporterUno.UITests.Helpers;

/// <summary>
/// Provides reusable UI test helper methods for common operations like waiting, tapping, and text entry.
/// Implements retry logic and proper error handling for robust test execution.
/// </summary>
public class UITestHelpers
{
    private readonly IApp _app;
    private readonly TimeSpan _defaultTimeout = TimeSpan.FromSeconds(30);
    private readonly TimeSpan _shortTimeout = TimeSpan.FromSeconds(10);
    private readonly TimeSpan _longTimeout = TimeSpan.FromSeconds(60);

    public UITestHelpers(IApp app)
    {
        _app = app ?? throw new ArgumentNullException(nameof(app));
    }

    #region Wait Methods

    /// <summary>
    /// Waits for an element to be present with specified timeout
    /// </summary>
    public void WaitForElement(string automationId, TimeSpan? timeout = null)
    {
        var waitTimeout = timeout ?? _defaultTimeout;

        try
        {
            _app.WaitForElement(
                x => x.Marked(automationId),
                timeout: waitTimeout,
                timeoutMessage: $"Element with AutomationId '{automationId}' was not found within {waitTimeout.TotalSeconds} seconds"
            );
        }
        catch (TimeoutException ex)
        {
            TestContext.WriteLine($"Timeout waiting for element: {automationId}");
            throw new AssertionException($"Element '{automationId}' not found", ex);
        }
    }

    /// <summary>
    /// Waits for an element to appear with specified text
    /// </summary>
    public void WaitForText(string text, TimeSpan? timeout = null)
    {
        var waitTimeout = timeout ?? _defaultTimeout;

        try
        {
            _app.WaitForElement(
                x => x.Text(text),
                timeout: waitTimeout,
                timeoutMessage: $"Text '{text}' was not found within {waitTimeout.TotalSeconds} seconds"
            );
        }
        catch (TimeoutException ex)
        {
            TestContext.WriteLine($"Timeout waiting for text: {text}");
            throw new AssertionException($"Text '{text}' not found", ex);
        }
    }

    /// <summary>
    /// Waits for an element to disappear
    /// </summary>
    public void WaitForElementToDisappear(string automationId, TimeSpan? timeout = null)
    {
        var waitTimeout = timeout ?? _defaultTimeout;
        var endTime = DateTime.Now.Add(waitTimeout);

        while (DateTime.Now < endTime)
        {
            var elements = _app.Query(x => x.Marked(automationId));
            if (elements.Length == 0)
            {
                return;
            }
            Thread.Sleep(500);
        }

        throw new AssertionException($"Element '{automationId}' did not disappear within {waitTimeout.TotalSeconds} seconds");
    }

    /// <summary>
    /// Waits for element to become enabled
    /// </summary>
    public void WaitForElementToBeEnabled(string automationId, TimeSpan? timeout = null)
    {
        var waitTimeout = timeout ?? _defaultTimeout;
        var endTime = DateTime.Now.Add(waitTimeout);

        while (DateTime.Now < endTime)
        {
            var elements = _app.Query(x => x.Marked(automationId));
            if (elements.Length > 0 && elements[0].Enabled)
            {
                return;
            }
            Thread.Sleep(500);
        }

        throw new AssertionException($"Element '{automationId}' did not become enabled within {waitTimeout.TotalSeconds} seconds");
    }

    /// <summary>
    /// Waits for a loading indicator to disappear
    /// </summary>
    public void WaitForLoadingComplete(string loadingIndicatorId = "LoadingIndicator", TimeSpan? timeout = null)
    {
        WaitForElementToDisappear(loadingIndicatorId, timeout ?? _longTimeout);
    }

    #endregion

    #region Interaction Methods

    /// <summary>
    /// Taps on an element identified by AutomationId
    /// </summary>
    public void TapElement(string automationId)
    {
        WaitForElement(automationId);

        try
        {
            _app.Tap(x => x.Marked(automationId));
            TestContext.WriteLine($"Tapped element: {automationId}");
        }
        catch (Exception ex)
        {
            TestContext.WriteLine($"Failed to tap element: {automationId}");
            throw new AssertionException($"Could not tap element '{automationId}'", ex);
        }
    }

    /// <summary>
    /// Taps on an element by text content
    /// </summary>
    public void TapByText(string text)
    {
        WaitForText(text);

        try
        {
            _app.Tap(x => x.Text(text));
            TestContext.WriteLine($"Tapped element with text: {text}");
        }
        catch (Exception ex)
        {
            TestContext.WriteLine($"Failed to tap element with text: {text}");
            throw new AssertionException($"Could not tap element with text '{text}'", ex);
        }
    }

    /// <summary>
    /// Double taps an element
    /// </summary>
    public void DoubleTapElement(string automationId)
    {
        WaitForElement(automationId);

        try
        {
            _app.DoubleTap(x => x.Marked(automationId));
            TestContext.WriteLine($"Double tapped element: {automationId}");
        }
        catch (Exception ex)
        {
            throw new AssertionException($"Could not double tap element '{automationId}'", ex);
        }
    }

    /// <summary>
    /// Enters text into a text box
    /// </summary>
    public void EnterText(string automationId, string text, bool clearFirst = true)
    {
        WaitForElement(automationId);

        try
        {
            if (clearFirst)
            {
                _app.ClearText(x => x.Marked(automationId));
            }

            _app.EnterText(x => x.Marked(automationId), text);
            TestContext.WriteLine($"Entered text into {automationId}: {text}");
        }
        catch (Exception ex)
        {
            TestContext.WriteLine($"Failed to enter text into: {automationId}");
            throw new AssertionException($"Could not enter text into '{automationId}'", ex);
        }
    }

    /// <summary>
    /// Clears text from a text box
    /// </summary>
    public void ClearText(string automationId)
    {
        WaitForElement(automationId);

        try
        {
            _app.ClearText(x => x.Marked(automationId));
            TestContext.WriteLine($"Cleared text from: {automationId}");
        }
        catch (Exception ex)
        {
            throw new AssertionException($"Could not clear text from '{automationId}'", ex);
        }
    }

    /// <summary>
    /// Scrolls to an element
    /// </summary>
    public void ScrollToElement(string automationId, string? withinContainerId = null)
    {
        try
        {
            if (withinContainerId != null)
            {
                _app.ScrollDownTo(x => x.Marked(automationId), x => x.Marked(withinContainerId));
            }
            else
            {
                _app.ScrollDownTo(x => x.Marked(automationId));
            }
            TestContext.WriteLine($"Scrolled to element: {automationId}");
        }
        catch (Exception ex)
        {
            throw new AssertionException($"Could not scroll to element '{automationId}'", ex);
        }
    }

    /// <summary>
    /// Scrolls down in a scrollable container
    /// </summary>
    public void ScrollDown(string? containerId = null)
    {
        try
        {
            if (containerId != null)
            {
                _app.ScrollDown(x => x.Marked(containerId));
            }
            else
            {
                _app.ScrollDown();
            }
            TestContext.WriteLine("Scrolled down");
        }
        catch (Exception ex)
        {
            throw new AssertionException("Could not scroll down", ex);
        }
    }

    /// <summary>
    /// Scrolls up in a scrollable container
    /// </summary>
    public void ScrollUp(string? containerId = null)
    {
        try
        {
            if (containerId != null)
            {
                _app.ScrollUp(x => x.Marked(containerId));
            }
            else
            {
                _app.ScrollUp();
            }
            TestContext.WriteLine("Scrolled up");
        }
        catch (Exception ex)
        {
            throw new AssertionException("Could not scroll up", ex);
        }
    }

    #endregion

    #region Assertion Methods

    /// <summary>
    /// Asserts that an element is displayed
    /// </summary>
    public void AssertElementDisplayed(string automationId)
    {
        var elements = _app.Query(x => x.Marked(automationId));

        if (elements.Length == 0)
        {
            throw new AssertionException($"Element '{automationId}' is not displayed");
        }

        TestContext.WriteLine($"Element is displayed: {automationId}");
    }

    /// <summary>
    /// Asserts that an element is not displayed
    /// </summary>
    public void AssertElementNotDisplayed(string automationId)
    {
        var elements = _app.Query(x => x.Marked(automationId));

        if (elements.Length > 0)
        {
            throw new AssertionException($"Element '{automationId}' is displayed but should not be");
        }

        TestContext.WriteLine($"Element is not displayed: {automationId}");
    }

    /// <summary>
    /// Asserts that text is displayed on the screen
    /// </summary>
    public void AssertTextDisplayed(string text)
    {
        var elements = _app.Query(x => x.Text(text));

        if (elements.Length == 0)
        {
            throw new AssertionException($"Text '{text}' is not displayed");
        }

        TestContext.WriteLine($"Text is displayed: {text}");
    }

    /// <summary>
    /// Asserts that text is not displayed on the screen
    /// </summary>
    public void AssertTextNotDisplayed(string text)
    {
        var elements = _app.Query(x => x.Text(text));

        if (elements.Length > 0)
        {
            throw new AssertionException($"Text '{text}' is displayed but should not be");
        }

        TestContext.WriteLine($"Text is not displayed: {text}");
    }

    /// <summary>
    /// Asserts that an element is enabled
    /// </summary>
    public void AssertElementEnabled(string automationId)
    {
        var elements = _app.Query(x => x.Marked(automationId));

        if (elements.Length == 0)
        {
            throw new AssertionException($"Element '{automationId}' not found");
        }

        if (!elements[0].Enabled)
        {
            throw new AssertionException($"Element '{automationId}' is not enabled");
        }

        TestContext.WriteLine($"Element is enabled: {automationId}");
    }

    /// <summary>
    /// Asserts that an element is disabled
    /// </summary>
    public void AssertElementDisabled(string automationId)
    {
        var elements = _app.Query(x => x.Marked(automationId));

        if (elements.Length == 0)
        {
            throw new AssertionException($"Element '{automationId}' not found");
        }

        if (elements[0].Enabled)
        {
            throw new AssertionException($"Element '{automationId}' is enabled but should be disabled");
        }

        TestContext.WriteLine($"Element is disabled: {automationId}");
    }

    /// <summary>
    /// Gets the text content of an element
    /// </summary>
    public string GetElementText(string automationId)
    {
        WaitForElement(automationId);

        var elements = _app.Query(x => x.Marked(automationId));

        if (elements.Length == 0)
        {
            throw new AssertionException($"Element '{automationId}' not found");
        }

        return elements[0].Text ?? string.Empty;
    }

    /// <summary>
    /// Asserts that an element has specific text
    /// </summary>
    public void AssertElementText(string automationId, string expectedText)
    {
        var actualText = GetElementText(automationId);

        if (actualText != expectedText)
        {
            throw new AssertionException($"Element '{automationId}' has text '{actualText}' but expected '{expectedText}'");
        }

        TestContext.WriteLine($"Element '{automationId}' has expected text: {expectedText}");
    }

    /// <summary>
    /// Checks if an element exists without throwing an exception
    /// </summary>
    public bool ElementExists(string automationId)
    {
        var elements = _app.Query(x => x.Marked(automationId));
        return elements.Length > 0;
    }

    #endregion

    #region Platform-Specific Methods

    /// <summary>
    /// Dismisses keyboard (mobile platforms)
    /// </summary>
    public void DismissKeyboard()
    {
        try
        {
            _app.DismissKeyboard();
            TestContext.WriteLine("Keyboard dismissed");
        }
        catch (Exception ex)
        {
            TestContext.WriteLine($"Could not dismiss keyboard: {ex.Message}");
        }
    }

    /// <summary>
    /// Takes a screenshot with optional description
    /// </summary>
    public FileInfo Screenshot(string title)
    {
        try
        {
            var screenshot = _app.Screenshot(title);
            TestContext.WriteLine($"Screenshot taken: {title}");
            return screenshot;
        }
        catch (Exception ex)
        {
            TestContext.WriteLine($"Failed to take screenshot: {ex.Message}");
            throw;
        }
    }

    #endregion

    #region Utility Methods

    /// <summary>
    /// Waits for a specified duration
    /// </summary>
    public void Wait(TimeSpan duration)
    {
        Thread.Sleep(duration);
        TestContext.WriteLine($"Waited for {duration.TotalSeconds} seconds");
    }

    /// <summary>
    /// Retries an action until it succeeds or timeout is reached
    /// </summary>
    public void RetryUntilSuccessOrTimeout(Action action, TimeSpan? timeout = null, TimeSpan? retryInterval = null)
    {
        var waitTimeout = timeout ?? _defaultTimeout;
        var interval = retryInterval ?? TimeSpan.FromMilliseconds(500);
        var endTime = DateTime.Now.Add(waitTimeout);

        Exception? lastException = null;

        while (DateTime.Now < endTime)
        {
            try
            {
                action();
                return;
            }
            catch (Exception ex)
            {
                lastException = ex;
                Thread.Sleep(interval);
            }
        }

        throw new AssertionException($"Action did not succeed within {waitTimeout.TotalSeconds} seconds", lastException);
    }

    /// <summary>
    /// Executes an action with retry logic
    /// </summary>
    public T RetryUntilSuccessOrTimeout<T>(Func<T> func, TimeSpan? timeout = null, TimeSpan? retryInterval = null)
    {
        var waitTimeout = timeout ?? _defaultTimeout;
        var interval = retryInterval ?? TimeSpan.FromMilliseconds(500);
        var endTime = DateTime.Now.Add(waitTimeout);

        Exception? lastException = null;

        while (DateTime.Now < endTime)
        {
            try
            {
                return func();
            }
            catch (Exception ex)
            {
                lastException = ex;
                Thread.Sleep(interval);
            }
        }

        throw new AssertionException($"Function did not succeed within {waitTimeout.TotalSeconds} seconds", lastException);
    }

    #endregion
}
