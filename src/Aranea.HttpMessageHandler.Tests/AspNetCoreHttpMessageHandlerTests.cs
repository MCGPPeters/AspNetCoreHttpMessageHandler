namespace Aranea.HttpMessageHandler.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Http;
    using Xunit;
    using HttpMessageHandler;
    using Eru;

    public class HttpMessageHandlerTests
    {
        [Theory(DisplayName = "When sending a request to an endpoint, an expected result should be returned")]
        [InlineData("foo")]
        [InlineData("bar")]
        [InlineData("so")]
        [InlineData("what")]
        public void Test1(string requestPath)
        {
            var httpMessageHandler = new AspNetCoreHttpMessageHandler(context =>
            {
                if (context.Request.Path == $"/{requestPath}")
                    context.Response.StatusCode = 200;

                return Task.FromResult((object) null);
            });

            var responseMessage = new HttpClient(httpMessageHandler)
                .Use(client => client.GetAsync($"http://sample.com/{requestPath}").Result);

            Assert.Equal(HttpStatusCode.OK, responseMessage.StatusCode);
        }

        [Theory(DisplayName = "Form data should be handled properly")]
        [InlineData("Maurice")]
        [InlineData("Damian")]
        [InlineData("Who")]
        [InlineData("Else")]
        public void Test2(string targetOfGreeting)
        {
            var httpMessageHandler = new AspNetCoreHttpMessageHandler
                (async context =>
                {
                    if (context.Request.Path == "/greeting")
                    {
                        var form = context.Request.ReadFormAsync().Result;
                        await context.Response.WriteAsync("Hello " + form["Name"]);
                    }
                });

            var responseMessage = new HttpClient(httpMessageHandler)
                .Use(client => 
                    client.PostAsync("http://sample.com/greeting", new FormUrlEncodedContent(new List<KeyValuePair<string, string>>
                    {
                        new KeyValuePair<string, string>("Name", targetOfGreeting)
                    }))
                .Result);

            Assert.Equal(responseMessage.Content.ReadAsStringAsync().Result, $"Hello {targetOfGreeting}");
        }

        [Theory(DisplayName =
            "The http context should contain the Content-Length header when the content isn't empty and it's value should be correct"
        )]
        [InlineData("Hello")]
        [InlineData("world")]
        [InlineData("anybody")]
        [InlineData("out there?")]
        public void Test3(string content)
        {
            HttpContext httpContext = null;
            var httpMessageHandler = new AspNetCoreHttpMessageHandler(context =>
            {
                httpContext = context;
                return Task.FromResult(0);
            });

            new HttpClient(httpMessageHandler)
                .Use(client =>
                {
                    var stringContent = new StringContent(content);
                    client.PostAsync("http://localhost/", stringContent).Wait();
                });

            Assert.True(httpContext.Request.Headers.ContainsKey("Content-Length"));
            Assert.Equal(httpContext.Request.ContentLength, content.Length);
        }

        private static readonly Func<HttpRequest, Task<string>> ReadToEnd = async request =>
        {
            using (var reader = new StreamReader(request.Body))
            {
                return await reader.ReadToEndAsync();
            }
        };

        private readonly RequestDelegate _requestDelegateForTestingRedirects = async context =>
        {
            if (context.Request.Path == "/redirect")
            {
                context.Response.StatusCode = 200;
                var requestBody = await ReadToEnd(context.Request);
                await context.Response.WriteAsync(requestBody);
            }

            if (context.Request.Path == "/redirect-loop")
            {
                context.Response.StatusCode = 302;
                context.Response.Headers.Add("Location", new[] {"http://localhost/redirect-loop"});
                await ReadToEnd(context.Request);
            }

            if (context.Request.Path == "/redirect-301-relative")
            {
                context.Response.StatusCode = 301;
                context.Response.Headers.Add("Location", new[] {"redirect"});
                await ReadToEnd(context.Request);
            }

            if (context.Request.Path == "/redirect-301-absolute-setcookie")
            {
                context.Response.StatusCode = 302;
                context.Response.Headers.Add("Location", new[] {"http://localhost/redirect"});
                context.Response.Headers.Add("Set-Cookie", new[] {"foo=bar"});
                await ReadToEnd(context.Request);
            }

            if (context.Request.Path == "/redirect-301-absolute")
            {
                context.Response.StatusCode = 301;
                context.Response.Headers.Add("Location", new[] {"http://localhost/redirect"});
                await ReadToEnd(context.Request);
            }

            if (context.Request.Path == "/redirect-302-relative")
            {
                context.Response.StatusCode = 302;
                context.Response.Headers.Add("Location", new[] {"redirect"});
                await ReadToEnd(context.Request);
            }

            if (context.Request.Path == "/redirect-302-relative-setcookie")
            {
                context.Response.StatusCode = 302;
                context.Response.Headers.Add("Location", new[] {"redirect"});
                context.Response.Headers.Add("Set-Cookie", new[] {"foo=bar"});
                await ReadToEnd(context.Request);
            }

            if (context.Request.Path == "/redirect-302-absolute")
            {
                context.Response.StatusCode = 302;
                context.Response.Headers.Add("Location", new[] {"http://localhost/redirect"});
                await ReadToEnd(context.Request);
            }

            if (context.Request.Path == "/redirect-303-relative")
            {
                context.Response.StatusCode = 303;
                context.Response.Headers.Add("Location", new[] {"redirect"});
                await ReadToEnd(context.Request);
            }

            if (context.Request.Path == "/redirect-303-absolute")
            {
                context.Response.StatusCode = 303;
                context.Response.Headers.Add("Location", new[] {"http://localhost/redirect"});
                await ReadToEnd(context.Request);
            }

            if (context.Request.Path == "/redirect-307-relative")
            {
                context.Response.StatusCode = 307;
                context.Response.Headers.Add("Location", new[] {"redirect"});
                await ReadToEnd(context.Request);
            }

            if (context.Request.Path == "/redirect-307-absolute")
            {
                context.Response.StatusCode = 307;
                context.Response.Headers.Add("Location", new[] {"http://localhost/redirect"});
                await ReadToEnd(context.Request);
            }
        };


        [Theory(DisplayName = "Autoredirection works for absolute paths")]
        [InlineData(307)]
        [InlineData(303)]
        [InlineData(302)]
        [InlineData(301)]
        public async Task Test4(int code)
        {
            var httpMessageHandler = CreateAspNetCoreHttpMessageHandlerForRedirectTesting();

            using (var client = new HttpClient(httpMessageHandler)
            {
                BaseAddress = new Uri("http://localhost")
            })
            {
                var response = await client.GetAsync($"/redirect-{code}-absolute");

                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal("http://localhost/redirect", response.RequestMessage.RequestUri.AbsoluteUri);
            }
        }


        [Theory(DisplayName = "Autoredirection works for relative paths")]
        [InlineData(301)]
        [InlineData(302)]
        [InlineData(303)]
        [InlineData(307)]
        public async Task Test5(int code)
        {
            var httpMessageHandler = CreateAspNetCoreHttpMessageHandlerForRedirectTesting();

            using (var client = new HttpClient(httpMessageHandler)
            {
                BaseAddress = new Uri("http://localhost")
            })
            {
                var response = await client.GetAsync($"/redirect-{code}-relative");

                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal("http://localhost/redirect", response.RequestMessage.RequestUri.AbsoluteUri);
            }
        }

        [Theory(DisplayName = "Request headers are retained on redirect")]
        [InlineData("Accept", "application/json")]
        [InlineData("Accept-Charset", "utf-8")]
        [InlineData("Accept-Encoding", "gzip, deflate")]
        [InlineData("Cache-Control", "no-cache")]
        [InlineData("User-Agent", "Mozilla/5.0 (X11; Linux x86_64; rv:12.0) Gecko/20100101 Firefox/21.0")]
        public async Task Test6(string header, string value)
        {
            var httpMessageHandler = CreateAspNetCoreHttpMessageHandlerForRedirectTesting();

            using (var client = new HttpClient(httpMessageHandler)
            {
                BaseAddress = new Uri("http://localhost")
            })
            {
                client.DefaultRequestHeaders.Add(header, value);
                var response = await client.GetAsync("/redirect-301-absolute");

                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal(client.DefaultRequestHeaders.GetValues(header),
                    response.RequestMessage.Headers.GetValues(header));
            }
        }

        private AspNetCoreHttpMessageHandler CreateAspNetCoreHttpMessageHandlerForRedirectTesting()
        {
            var httpMessageHandler = new AspNetCoreHttpMessageHandler(_requestDelegateForTestingRedirects)
            {
                AllowAutoRedirect = true
            };

            return httpMessageHandler;
        }

        [Theory(DisplayName = "When redirecting, cookies should be passed along")]
        [InlineData("/redirect-301-absolute-setcookie")]
        [InlineData("/redirect-302-relative-setcookie")]
        public async Task Test15(string path)
        {
            var httpMessageHandler = CreateAspNetCoreHttpMessageHandlerForRedirectTesting();
            httpMessageHandler.UseCookies(true);

            using (var client = new HttpClient(httpMessageHandler)
            {
                BaseAddress = new Uri("http://localhost")
            })
            {
                var response = await client.GetAsync(path);

                Assert.Equal("foo=bar", response.RequestMessage.Headers.GetValues("Cookie").Single());
            }
        }

        [Fact(DisplayName = "When the headers are flushed, the response should complete")]
        public async Task Should_complete_when_headers_are_flushed()
        {
            var tcs = new TaskCompletionSource<int>(0);
            RequestDelegate requestDelegate = async context =>
            {
                context.Response.StatusCode = 200;
                await context.Response.WriteAsync("Blurg"); // Writing to response stream should flush the headers.
                await tcs.Task;
            };

            var httpMessageHandler = new AspNetCoreHttpMessageHandler(requestDelegate);
            httpMessageHandler.UseCookies(true);

            var httpClient = new HttpClient(httpMessageHandler);

            var responseTask = httpClient.GetAsync("http://example.com", HttpCompletionOption.ResponseHeadersRead);
            if (await Task.WhenAny(responseTask, Task.Delay(5000)) != responseTask)
                throw new TimeoutException("responseTask did not complete");
            tcs.SetResult(0);
        }

        [Fact(DisplayName = "When changing the use of cookies after initial operation, it is not allowed")]
        public async Task Test12()
        {
            var httpMessageHandler = new AspNetCoreHttpMessageHandler(_ => Task.FromResult(0));
            await new HttpClient(httpMessageHandler)
                .Use(async client =>
                {
                    await client.GetAsync("http://localhost/");
                    httpMessageHandler
                        .UseCookies(true)
                        .Otherwise(error =>
                        {
                            Assert.Equal("It is not permitted to change cookie usage after initial operation",
                                error.Message);
                            return Unit.Instance;
                        });
                });
        }

        [Fact(DisplayName = "When changing the use of cookies after disposal of the handler, it is not allowed")]
        public async Task Test13()
        {
            var httpMessageHandler = new AspNetCoreHttpMessageHandler(_ => Task.FromResult(0));

            using (var httpClient = new HttpClient(httpMessageHandler))
            {
                await httpClient.GetAsync("http://localhost/");
            }

            httpMessageHandler
                .UseCookies(true)
                .Otherwise(error =>
                 {
                     Assert.Equal("It is not permitted to change cookie usage after disposing", error.Message);
                     return Unit.Instance;
                 });
        }

        [Fact(
            DisplayName =
                "When caught in a redirect loop, the maximum allowed number of redirects should not be exceeded")]
        public void Test14()
        {
            var httpMessageHandler = CreateAspNetCoreHttpMessageHandlerForRedirectTesting();
            const int maximumNumberOfRedirects = 20;
            httpMessageHandler.AutoRedirectLimit = maximumNumberOfRedirects;

            new HttpClient(httpMessageHandler)
                .Use(async client =>
                {
                    client.BaseAddress = new Uri("http://localhost");

                    var response = await client.GetAsync("/redirect-loop");
                    var body = await response.Content.ReadAsStringAsync();
                    var actualProblemDetails = SimpleJson.DeserializeObject<HttpProblemDetails>(body,
                        new CamelCasingSerializerStrategy());
                    var expectedHttpProblemDetails = new HttpProblemDetails
                    {
                        Detail =
                            $"The number of details exceeded the maximum allowed number of {maximumNumberOfRedirects}",
                        Status = 500,
                        Title = "Too many redirects"
                    };

                    Assert.Equal(expectedHttpProblemDetails, actualProblemDetails,
                        new HttpProblemDetailsEqualityComparer());
                    Assert.Equal("application/problem+json", response.Content.Headers.ContentType.MediaType);
                });
        }

        [Fact(DisplayName = "Cookies that are set on the server should be in the cookie container")]
        public async Task Test16()
        {
            const string cookieName1 = "testcookie1";
            const string cookieName2 = "testcookie2";
            var httpMessageHandler = new AspNetCoreHttpMessageHandler(context =>
            {
                context.Response.Cookies.Append(cookieName1, "c1");
                context.Response.Cookies.Append(cookieName2, "c2");
                return Task.FromResult(0);
            });

            httpMessageHandler.UseCookies(true);

            var uri = new Uri("http://localhost/");

            using (var httpClient = new HttpClient(httpMessageHandler))
            {
                await httpClient.GetAsync(uri);
            }

            Assert.NotNull(httpMessageHandler
                .CookieContainer
                .GetCookies(uri)[cookieName1]);

            Assert.NotNull(httpMessageHandler
                .CookieContainer
                .GetCookies(uri)[cookieName1]
                .Value);

            Assert.NotNull(httpMessageHandler
                .CookieContainer
                .GetCookies(uri)[cookieName2]);

            Assert.NotNull(httpMessageHandler
                .CookieContainer
                .GetCookies(uri)[cookieName2]
                .Value);
        }

        [Fact(DisplayName = "When the headers are going to be sent, the cookie should be in the container")]
        public void Test17()
        {
            const string cookieName1 = "testcookie1";

            var uri = new Uri("http://localhost/");

            async Task Inner(HttpContext context)
            {
                context.Response.Headers.Append("Location", "/");
                await context.Response.WriteAsync("Test");
            }

            async Task RequestDelegate(HttpContext context)
            {
                context.Response.OnStarting(_ =>
                {
                    context.Response.Cookies.Append(cookieName1, "c1");
                    return Task.FromResult(0);
                }, null);
                await Inner(context);
            }

            var httpMessageHandler = new AspNetCoreHttpMessageHandler(RequestDelegate);

            httpMessageHandler.UseCookies(true);

            var response = new HttpClient(httpMessageHandler)
                .Use(async client =>
                {
                    await client.GetAsync(uri);
                    Assert.NotNull(httpMessageHandler
                        .CookieContainer
                        .GetCookies(uri)[cookieName1]
                        .Value);
                });

           
        }

        [Fact(DisplayName = "Authorization header is removed on redirect")]
        public async Task Test7()
        {
            var httpMessageHandler = CreateAspNetCoreHttpMessageHandlerForRedirectTesting();

            var responseMessage = await new HttpClient(httpMessageHandler).Use(async client =>
            {
                client.BaseAddress = new Uri("http://localhost");
                client.DefaultRequestHeaders.Authorization = AuthenticationHeaderValue.Parse("foo");
                return await client.GetAsync("/redirect-301-absolute");
            });

            Assert.Equal(HttpStatusCode.OK, responseMessage.StatusCode);
            Assert.Equal(null, responseMessage.RequestMessage.Headers.Authorization);
        }

        [Fact(DisplayName = "Redirect does not take place on POST and 307")]
        public void Test8()
        {
            var httpMessageHandler = CreateAspNetCoreHttpMessageHandlerForRedirectTesting();

            new HttpClient(httpMessageHandler).Use(async client =>
            {
                client.BaseAddress = new Uri("http://localhost");
                var responseMessage = await  client.PostAsync("/redirect-307-absolute", new StringContent("the-body"));
                Assert.Equal(HttpStatusCode.TemporaryRedirect, responseMessage.StatusCode);
                Assert.Equal("http://localhost/redirect-307-absolute", responseMessage.RequestMessage.RequestUri.AbsoluteUri);
            });
        }

        [Fact(DisplayName = "Redirect does not alter HTTP method")]
        public void Test9()
        {
            var httpMessageHandler = CreateAspNetCoreHttpMessageHandlerForRedirectTesting();

            new HttpClient(httpMessageHandler).Use(async client =>
            {
                client.BaseAddress = new Uri("http://localhost");
                var responseMessage = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, "/redirect-307-absolute"));
                Assert.Equal(HttpStatusCode.OK, responseMessage.StatusCode);
                Assert.Equal("http://localhost/redirect", responseMessage.RequestMessage.RequestUri.AbsoluteUri);
                Assert.Equal(HttpMethod.Head, responseMessage.RequestMessage.Method);
            });


            
        }
    }
}