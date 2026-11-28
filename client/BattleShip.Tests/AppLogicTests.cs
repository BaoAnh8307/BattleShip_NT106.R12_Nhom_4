using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using BattleShip.App;
using BattleShip.Network;

namespace BattleShip.Tests;

/// <summary>Kiểm thử phần logic của form đăng nhập, đăng ký (không cần giao diện).</summary>
public class InputRulesTests
{
    [Fact]
    public void ValidateUsername_AcceptsLettersDigitsUnderscore()
    {
        Assert.Null(InputRules.ValidateUsername("khoi01"));
        Assert.Null(InputRules.ValidateUsername("a_b"));
        Assert.Null(InputRules.ValidateUsername("  nam_02  ")); // khoảng trắng hai đầu được cắt
        Assert.Null(InputRules.ValidateUsername(new string('a', 20)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ab")]
    [InlineData("khoi 01")]
    [InlineData("khôi01")]
    [InlineData("khoi-01")]
    public void ValidateUsername_RejectsBadNames(string? name)
    {
        Assert.NotNull(InputRules.ValidateUsername(name));
    }

    [Fact]
    public void ValidateUsername_RejectsTooLong()
    {
        Assert.NotNull(InputRules.ValidateUsername(new string('a', 21)));
    }

    [Fact]
    public void ValidatePassword_AcceptsSixToSixtyFourCharacters()
    {
        Assert.Null(InputRules.ValidatePassword("123456"));
        Assert.Null(InputRules.ValidatePassword(new string('x', 64)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12345")]
    public void ValidatePassword_RejectsEmptyOrShort(string? password)
    {
        Assert.NotNull(InputRules.ValidatePassword(password));
    }

    [Fact]
    public void ValidatePassword_RejectsTooLong()
    {
        Assert.NotNull(InputRules.ValidatePassword(new string('x', 65)));
    }

    [Fact]
    public void ValidateConfirm_MustMatchPassword()
    {
        Assert.Null(InputRules.ValidateConfirm("abcdef", "abcdef"));
        Assert.NotNull(InputRules.ValidateConfirm("abcdef", "abcdeg"));
        Assert.NotNull(InputRules.ValidateConfirm("abcdef", ""));
    }
}

public class ErrorTextsTests
{
    [Fact]
    public void FromException_ServerError_UsesVietnameseTextForKnownCode()
    {
        var text = ErrorTexts.FromException(new ServerErrorException(ErrorCodes.AuthFailed, "wrong"));
        Assert.Equal(ErrorTexts.FromCode(ErrorCodes.AuthFailed), text);
        Assert.False(text.Contains("wrong"));
    }

    [Fact]
    public void FromCode_UnknownCode_FallsBackToServerMessage()
    {
        Assert.Equal("Thông điệp riêng", ErrorTexts.FromCode("SOMETHING_NEW", "Thông điệp riêng"));
        Assert.True(ErrorTexts.FromCode("SOMETHING_NEW").Contains("SOMETHING_NEW"));
    }

    [Fact]
    public void FromException_NetworkProblems_MentionServerAddress()
    {
        var config = new AppConfig { ServerHost = "10.0.0.5", ServerPort = 6000 };
        var text = ErrorTexts.FromException(new IOException("refused"), config);
        Assert.True(text.Contains("10.0.0.5:6000"));
    }

    [Fact]
    public void FromException_Timeout_HasTimeoutMessage()
    {
        Assert.True(ErrorTexts.FromException(new OperationCanceledException()).Contains("Hết thời gian"));
        Assert.True(ErrorTexts.FromException(new TimeoutException()).Contains("Hết thời gian"));
    }

    [Fact]
    public void FromException_OtherErrors_ShowTheirMessage()
    {
        Assert.True(ErrorTexts.FromException(new ArgumentException("boom")).Contains("boom"));
    }
}

public class AppConfigTests
{
    [Fact]
    public void Parse_ValidJson_ReadsAllFields()
    {
        var config = AppConfig.Parse("{\"UseMock\": false, \"ServerHost\": \"192.168.1.10\", \"ServerPort\": 6000}");
        Assert.False(config.UseMock);
        Assert.Equal("192.168.1.10", config.ServerHost);
        Assert.Equal(6000, config.ServerPort);
    }

    [Fact]
    public void Parse_IsCaseInsensitive_AndAllowsComments()
    {
        var config = AppConfig.Parse("{ // ghi chú\n \"usemock\": false, \"serverhost\": \"h\", }");
        Assert.False(config.UseMock);
        Assert.Equal("h", config.ServerHost);
    }

    [Fact]
    public void Parse_MissingFields_UseDefaults()
    {
        var config = AppConfig.Parse("{}");
        Assert.True(config.UseMock);
        Assert.Equal(AppConfig.DefaultHost, config.ServerHost);
        Assert.Equal(AppConfig.DefaultPort, config.ServerPort);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("không phải json")]
    public void Parse_EmptyOrBrokenJson_ReturnsDefaults(string? json)
    {
        var config = AppConfig.Parse(json);
        Assert.True(config.UseMock);
        Assert.Equal(AppConfig.DefaultPort, config.ServerPort);
    }

    [Fact]
    public void Parse_InvalidPortOrBlankHost_FallBackToDefaults()
    {
        var config = AppConfig.Parse("{\"ServerHost\": \"  \", \"ServerPort\": 99999}");
        Assert.Equal(AppConfig.DefaultHost, config.ServerHost);
        Assert.Equal(AppConfig.DefaultPort, config.ServerPort);
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var empty = Path.Combine(Path.GetTempPath(), "battleship_no_config_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(empty);
        try
        {
            Assert.Equal(AppConfig.DefaultPort, AppConfig.Load(empty).ServerPort);
        }
        finally
        {
            Directory.Delete(empty);
        }
    }

    [Fact]
    public void Load_ReadsFileFromDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "battleship_config_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, AppConfig.FileName), "{\"ServerPort\": 7000}");
            Assert.Equal(7000, AppConfig.Load(dir).ServerPort);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}

/// <summary>AppSession dùng trạng thái tĩnh, nên mọi bài kiểm thử của nó nằm chung một lớp (chạy tuần tự).</summary>
public class AppSessionTests
{
    private static INetworkClient FastMock(AppConfig _) => new MockNetworkClient(null, 1) { Latency = TimeSpan.Zero };

    [Fact]
    public async Task EnsureConnected_ConnectsOnlyWhenDisconnected()
    {
        AppSession.Start(new AppConfig(), FastMock);
        Assert.Equal(SessionState.Disconnected, AppSession.Client.State);

        await AppSession.EnsureConnectedAsync();
        Assert.Equal(SessionState.Connected, AppSession.Client.State);

        await AppSession.EnsureConnectedAsync(); // gọi lại không lỗi, không kết nối lần hai
        Assert.Equal(SessionState.Connected, AppSession.Client.State);
    }

    [Fact]
    public async Task Login_ThroughSession_StoresCurrentUser_AndLogoutClearsIt()
    {
        AppSession.Start(new AppConfig(), FastMock);
        await AppSession.EnsureConnectedAsync();

        AppSession.CurrentUser = await AppSession.Client.LoginAsync("demo", "demo123");
        Assert.Equal("demo", AppSession.CurrentUser.Username);
        Assert.Equal(SessionState.Authed, AppSession.Client.State);

        await AppSession.LogoutAsync();
        Assert.Null(AppSession.CurrentUser);
        Assert.Equal(SessionState.Disconnected, AppSession.Client.State);
    }

    [Fact]
    public async Task Register_ThroughSession_ThenLoginWithNewAccount()
    {
        AppSession.Start(new AppConfig(), FastMock);
        await AppSession.EnsureConnectedAsync();

        await AppSession.Client.RegisterAsync("khoi01", "123456");
        var info = await AppSession.Client.LoginAsync("khoi01", "123456");
        Assert.Equal("khoi01", info.Username);
    }

    [Fact]
    public async Task ServerErrors_AreTranslatedForDisplay()
    {
        AppSession.Start(new AppConfig(), FastMock);
        await AppSession.EnsureConnectedAsync();

        var ex = await Assert.ThrowsAsync<ServerErrorException>(() => AppSession.Client.LoginAsync("demo", "sai"));
        Assert.Equal(ErrorTexts.FromCode(ErrorCodes.AuthFailed), ErrorTexts.FromException(ex));

        var taken = await Assert.ThrowsAsync<ServerErrorException>(() => AppSession.Client.RegisterAsync("demo", "123456"));
        Assert.Equal(ErrorTexts.FromCode(ErrorCodes.UsernameTaken), ErrorTexts.FromException(taken));
    }

    [Fact]
    public void Start_WithUseMock_CreatesMockClient()
    {
        AppSession.Start(new AppConfig { UseMock = true });
        Assert.True(AppSession.Client is MockNetworkClient);
    }

    [Fact]
    public async Task Start_WithUseMockFalse_CreatesTheRealNetworkClient_StartingDisconnected()
    {
        try
        {
            AppSession.Start(new AppConfig { UseMock = false });

            Assert.True(AppSession.Client is NetworkClient);
            Assert.False(AppSession.Client is MockNetworkClient);
            Assert.Equal(SessionState.Disconnected, AppSession.Client.State);
        }
        finally
        {
            await AppSession.ShutdownAsync();
        }
    }

    [Fact]
    public async Task EnsureConnected_WhenNobodyListens_FailsFast_AndStaysDisconnected()
    {
        var port = FreeClosedPort();
        try
        {
            AppSession.Start(new AppConfig { UseMock = false, ServerHost = "127.0.0.1", ServerPort = port });

            await Assert.ThrowsAnyAsync<Exception>(() => AppSession.EnsureConnectedAsync(TimeSpan.FromSeconds(3)));
            Assert.Equal(SessionState.Disconnected, AppSession.Client.State);
        }
        finally
        {
            await AppSession.ShutdownAsync();
        }
    }

    [Fact]
    public async Task RealClient_LoginRoundTrip_UsesNewlineFramedJson_WithALoopbackServer()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        // Server giả nói đúng giao thức: đọc MỘT dòng JSON, trả LOGIN_OK có "ref" = seq của yêu cầu.
        var received = Task.Run(async () =>
        {
            using var socket = await listener.AcceptTcpClientAsync();
            using var stream = socket.GetStream();
            var utf8 = new UTF8Encoding(false);
            using var reader = new StreamReader(stream, utf8);

            var line = await reader.ReadLineAsync();
            using var request = JsonDocument.Parse(line!);
            var seq = request.RootElement.GetProperty("seq").GetInt32();

            var reply = $"{{\"type\":\"LOGIN_OK\",\"seq\":1,\"ref\":{seq},\"data\":{{\"user_id\":7,\"username\":\"demo\"}}}}\n";
            await stream.WriteAsync(utf8.GetBytes(reply));
            await stream.FlushAsync();
            return line!;
        });

        try
        {
            AppSession.Start(new AppConfig { UseMock = false, ServerHost = "127.0.0.1", ServerPort = port });
            await AppSession.EnsureConnectedAsync();
            Assert.Equal(SessionState.Connected, AppSession.Client.State);

            var info = await AppSession.Client.LoginAsync("demo", "demo123");

            Assert.Equal("demo", info.Username);
            Assert.Equal(7, info.UserId);
            Assert.Equal(SessionState.Authed, AppSession.Client.State);

            using var sent = JsonDocument.Parse(await received.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal("LOGIN", sent.RootElement.GetProperty("type").GetString());
            Assert.Equal("demo", sent.RootElement.GetProperty("data").GetProperty("username").GetString());
            Assert.Equal(1, sent.RootElement.GetProperty("data").GetProperty("version").GetInt32());
        }
        finally
        {
            await AppSession.ShutdownAsync();
        }
    }

    /// <summary>Lấy một cổng vừa được hệ điều hành cấp rồi đóng ngay, tức là chắc chắn không ai lắng nghe.</summary>
    private static int FreeClosedPort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }
}