using FlowBlox.Core.Util.Downloads;
using FlowBlox.Core.Models.FlowBlocks.Web.InternalWebBrowser;
using FlowBlox.Core.Models.FlowBlocks.Web;
using Newtonsoft.Json;
using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;
using Cookie = OpenQA.Selenium.Cookie;

namespace FlowBlox.Core.Models.FlowBlocks.WebBrowser
{
    public abstract class WebBrowserBase
    {
        protected WebDriverWait webDriverWait;
        protected IWebDriver driver;

        protected WebBrowserBase()
        {
            
        }

        protected void InitWebDriverWait()
        {
            var timeout = TimeSpan.FromSeconds(WebBrowserConstants.DefaultTimeout);
            this.webDriverWait = new WebDriverWait(driver, timeout);
        }

        /// <summary>
        /// Convenience accessor for executing JavaScript on the current driver.
        /// </summary>
        protected IJavaScriptExecutor JsExecutor => (IJavaScriptExecutor)driver;

        public string Name { get; set; }

        protected TResult DeserializeToSettings<TResult>(string serializedSettings)
            where TResult : SeleniumWebBrowserSettingsDto
        {
            TResult settings;
            try
            {
                settings = JsonConvert.DeserializeObject<TResult>(serializedSettings);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException(
                    "Option 'WebBrowser.ServiceSettings' contains invalid JSON. " +
                    "Please fix the JSON or reset it to default.", ex);
            }

            if (settings == null)
            {
                throw new InvalidOperationException(
                    "Deserialization of 'WebBrowser.ServiceSettings' returned null. " +
                    "Please check the JSON configuration or reset it to default.");
            }

            return settings;
        }

        public virtual int Timeout
        {
            get
            {
                return this.webDriverWait.Timeout.Seconds;
            }
            set
            {
                this.webDriverWait.Timeout = TimeSpan.FromSeconds(value);
            }
        }

        public virtual void Open(string url)
        {
            driver.Navigate().GoToUrl(url);
            WaitForPageLoad();
        }

        public virtual void Close()
        {
            driver.Close();
        }

        private IWebElement FindElement(string selector, WebEventSelectionMode mode, int retryCount = 0)
        {
            return mode == WebEventSelectionMode.XPath ?
                    driver.FindElement(By.XPath(selector)) :
                    driver.FindElement(By.CssSelector(selector));
        }

        private IEnumerable<IWebElement> FindElements(string selector, WebEventSelectionMode mode, int retryCount = 0)
        {
            return mode == WebEventSelectionMode.XPath ?
                driver.FindElements(By.XPath(selector)) :
                driver.FindElements(By.CssSelector(selector));
        }

        public int RetryLimit { get; set; }

        public int RetryTimeunit { get; set; }

        /// <summary>
        /// Executes a browser action with retry support.
        /// Retries common transient Selenium errors (element not found, not interactable, stale, intercepted).
        /// </summary>
        protected WebBrowserActionResult Invoke(Func<WebBrowserActionResult> executor, int retryCount = 0)
        {
            try
            {
                return executor();
            }
            catch (NoSuchElementException ex)
            {
                return HandleRetryOrFail(executor, retryCount, ex, WebBrowserActionStatus.ElementNotFound);
            }
            catch (ElementNotInteractableException ex)
            {
                return HandleRetryOrFail(executor, retryCount, ex, WebBrowserActionStatus.ElementNotApplicable);
            }
            catch (StaleElementReferenceException ex)
            {
                return HandleRetryOrFail(executor, retryCount, ex, WebBrowserActionStatus.ElementStale);
            }
            catch (WebDriverException ex)
            {
                return HandleRetryOrFail(executor, retryCount, ex, WebBrowserActionStatus.UnknownError);
            }
        }

        private WebBrowserActionResult HandleRetryOrFail(
            Func<WebBrowserActionResult> executor,
            int retryCount,
            Exception exception,
            WebBrowserActionStatus status)
        {
            if (retryCount < RetryLimit)
            {
                WaitForPageLoad();
                Thread.Sleep(RetryTimeunit);
                return Invoke(executor, retryCount + 1);
            }

            return new WebBrowserActionResult
            {
                Success = false,
                Exception = exception,
                Status = status
            };
        }

        public virtual WebBrowserActionResult ClickOnElement(string selector, WebEventSelectionMode mode)
        {
            return this.Invoke(() =>
            {
                var elem = FindElement(selector, mode);
                elem.Click();
                WaitForPageLoad();
                return new WebBrowserActionResult()
                {
                    Success = true
                };
            });
        }

        public virtual WebBrowserActionResult ClickOnAllElements(string selector, WebEventSelectionMode mode)
        {
            return this.Invoke(() =>
            {
                foreach (var elem in FindElements(selector, mode))
                {
                    elem.Click();
                    WaitForPageLoad();
                }

                return new WebBrowserActionResult()
                {
                    Success = true
                };
            });
        }

        public virtual WebBrowserActionResult ScrollMax()
        {
            JsExecutor.ExecuteScript("window.scrollTo(0, document.body.scrollHeight);");
            WaitForPageLoad();
            return new WebBrowserActionResult()
            {
                Success = true
            };
        }

        public string TabHandle => driver.WindowHandles.Last();

        public virtual int Height => driver.Manage().Window.Size.Height;

        public virtual string DOMContent => driver.PageSource;

        public virtual void Dispose()
        {
            this.driver?.Quit();
            this.driver?.Dispose();
        }

        public virtual WebBrowserActionResult EnterTextForElement(string selector, string text, WebEventSelectionMode mode)
        {
            return this.Invoke(() =>
            {
                var elem = FindElement(selector, mode);
                elem.SendKeys(text);
                return new WebBrowserActionResult()
                {
                    Success = true
                };
            });
        }

        public virtual WebBrowserActionResult SendSpecialKey(
            string selector,
            WebEventSelectionMode mode,
            WebSpecialKey key,
            WebSpecialKeyModifier modifier)
        {
            return this.Invoke(() =>
            {
                var element = FindElement(selector, mode);
                var keyToken = GetSeleniumKey(key);
                var modifierKeys = GetModifierKeys(modifier);

                var actions = new OpenQA.Selenium.Interactions.Actions(driver);
                actions.MoveToElement(element).Click(element);

                foreach (var modifierKey in modifierKeys)
                    actions.KeyDown(modifierKey);

                actions.SendKeys(element, keyToken);

                foreach (var modifierKey in modifierKeys.Reverse())
                    actions.KeyUp(modifierKey);

                actions.Perform();

                return new WebBrowserActionResult
                {
                    Success = true
                };
            });
        }

        public virtual void DeleteAllCookies()
        {
            this.driver.Manage().Cookies.DeleteAllCookies();
        }

        private static string GetSeleniumKey(WebSpecialKey key)
        {
            return key switch
            {
                WebSpecialKey.Enter => Keys.Enter,
                WebSpecialKey.Tab => Keys.Tab,
                WebSpecialKey.Escape => Keys.Escape,
                WebSpecialKey.Backspace => Keys.Backspace,
                WebSpecialKey.Delete => Keys.Delete,
                WebSpecialKey.ArrowUp => Keys.ArrowUp,
                WebSpecialKey.ArrowDown => Keys.ArrowDown,
                WebSpecialKey.ArrowLeft => Keys.ArrowLeft,
                WebSpecialKey.ArrowRight => Keys.ArrowRight,
                WebSpecialKey.Space => Keys.Space,
                _ => Keys.Enter
            };
        }

        private static IReadOnlyList<string> GetModifierKeys(WebSpecialKeyModifier modifier)
        {
            return modifier switch
            {
                WebSpecialKeyModifier.Ctrl => [Keys.Control],
                WebSpecialKeyModifier.Shift => [Keys.Shift],
                WebSpecialKeyModifier.Alt => [Keys.Alt],
                WebSpecialKeyModifier.CtrlShift => [Keys.Control, Keys.Shift],
                WebSpecialKeyModifier.CtrlAlt => [Keys.Control, Keys.Alt],
                WebSpecialKeyModifier.ShiftAlt => [Keys.Shift, Keys.Alt],
                WebSpecialKeyModifier.CtrlShiftAlt => [Keys.Control, Keys.Shift, Keys.Alt],
                _ => []
            };
        }

        public virtual List<Cookie> GetAllCookies()
        {
            return driver.Manage().Cookies.AllCookies.ToList();
        }

        public virtual void SetCookies(List<OpenQA.Selenium.Cookie> cookies)
        {
            foreach (var cookie in cookies)
            {
                var adjustedCookie = new OpenQA.Selenium.Cookie(
                    cookie.Name,
                    cookie.Value,
                    cookie.Domain,
                    cookie.Path,
                    cookie.Expiry);

                driver.Manage().Cookies.AddCookie(adjustedCookie);
            }
        }

        public virtual WebBrowserActionResult UpdateDOM(string selector, string content, WebEventSelectionMode mode)
        {
            return this.Invoke(() =>
            {
                var elem = FindElement(selector, mode);

                // Escape the content string for JavaScript
                string escapedContent = content.Replace("\\", "\\\\")
                    .Replace("'", "\\'")
                    .Replace("\r\n", "\\n")
                    .Replace("\n", "\\n")
                    .Replace("\r", "\\n");

                JsExecutor.ExecuteScript($"arguments[0].innerHTML = '{escapedContent}'", elem);
                return new WebBrowserActionResult()
                {
                    Success = true
                };
            });
        }

        public virtual WebBrowserActionResult UploadFile(string selector, WebEventSelectionMode mode, string filePath)
        {
            return this.Invoke(() =>
            {
                IWebElement fileInput = null;
                if (mode == WebEventSelectionMode.CssSelector)
                    fileInput = driver.FindElement(By.CssSelector(selector));
                else if (mode == WebEventSelectionMode.XPath)
                    fileInput = driver.FindElement(By.XPath(selector));

                if (fileInput != null && fileInput.GetAttribute("type") == "file")
                {
                    fileInput.SendKeys(filePath);
                    return new WebBrowserActionResult()
                    {
                        Success = true
                    };
                }
                else
                {
                    return new WebBrowserActionResult()
                    {
                        Success = false,
                        Status = WebBrowserActionStatus.ElementNotFound
                    };
                }
            });
        }

        public virtual WebBrowserDownloadActionResult DownloadFile(
            string selector,
            WebEventSelectionMode mode,
            WebDownloadMode downloadMode,
            string downloadPath,
            string downloadDirectory,
            int timeoutSeconds)
        {
            return DownloadFile(selector, mode, downloadMode, downloadPath, downloadDirectory, timeoutSeconds, null, default);
        }

        public virtual WebBrowserDownloadActionResult DownloadFile(
            string selector,
            WebEventSelectionMode mode,
            WebDownloadMode downloadMode,
            string downloadPath,
            string downloadDirectory,
            int timeoutSeconds,
            TransferProgressReporter? progress,
            CancellationToken cancellationToken)
        {
            var handler = new WebBrowserDownloadFileHandler(driver, timeoutSeconds, progress, cancellationToken);
            return handler.DownloadFile(selector, mode, downloadMode, downloadPath, downloadDirectory);
        }

        public virtual WebBrowserActionResult SwitchToUrl(string url)
        {
            try
            {
                driver.Navigate().GoToUrl(url);
                WaitForPageLoad();
                return new WebBrowserActionResult()
                {
                    Success = true
                };
            }
            catch (Exception e)
            {
                return new WebBrowserActionResult()
                {
                    Success = false,
                    Status = WebBrowserActionStatus.UrlNotAccessible,
                    Exception = e
                };
            }
        }

        private void WaitForPageLoad()
        {
            webDriverWait.Until(driver =>
            {
                bool isPageLoaded = ((IJavaScriptExecutor)driver).ExecuteScript("return document.readyState").Equals("complete");
                bool isBodyPresent = ((IJavaScriptExecutor)driver).ExecuteScript("return document.body != null").Equals(true);
                return isPageLoaded && isBodyPresent;
            });
        }

        public virtual WebBrowserContentActionResult GetContent(string selector, WebEventSelectionMode mode, bool innerContent)
        {
            string content = "";
            var actionResult = this.Invoke(() =>
            {
                var elem = FindElement(selector, mode);
                content = innerContent ? 
                    elem.GetAttribute("innerHTML") : 
                    elem.GetAttribute("outerHTML");

                return new WebBrowserActionResult()
                {
                    Success = true
                };
            });
            return new WebBrowserContentActionResult()
            {
                Content = content,
                Exception = actionResult.Exception,
                Status = actionResult.Status,
                Success = actionResult.Success
            };
        }

        public virtual WebBrowserContentsActionResult GetContents(string selector, WebEventSelectionMode mode, bool innerContent)
            => GetContents(selector, mode, innerContent, WebSelectorOutputMode.Element, null);

        public virtual WebBrowserContentsActionResult GetContents(
            string selector,
            WebEventSelectionMode mode,
            bool innerContent,
            WebSelectorOutputMode outputMode,
            string attributeName)
        {
            List<string> contents = new();
            var actionResult = this.Invoke(() =>
            {
                var elements = FindElements(selector, mode)?.ToList() ?? new List<IWebElement>();
                if (elements.Count == 0)
                {
                    return new WebBrowserActionResult
                    {
                        Success = false,
                        Status = WebBrowserActionStatus.ElementNotFound
                    };
                }

                contents = elements
                    .Select(elem => GetElementContent(elem, innerContent, outputMode, attributeName))
                    .ToList();

                return new WebBrowserActionResult
                {
                    Success = true
                };
            });

            return new WebBrowserContentsActionResult
            {
                Contents = contents,
                Exception = actionResult.Exception,
                Status = actionResult.Status,
                Success = actionResult.Success
            };
        }

        private static string GetElementContent(
            IWebElement element,
            bool innerContent,
            WebSelectorOutputMode outputMode,
            string attributeName)
        {
            return outputMode switch
            {
                WebSelectorOutputMode.Attribute => element.GetAttribute(attributeName) ?? string.Empty,
                _ => innerContent
                    ? element.GetAttribute("innerHTML")
                    : element.GetAttribute("outerHTML")
            } ?? string.Empty;
        }
    }
}
