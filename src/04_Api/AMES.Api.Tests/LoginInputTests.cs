using AMES.Api.Auth;
using AMES.Contracts.Dto;
using Xunit;

namespace AMES.Api.Tests;

public class LoginInputTests
{
    [Theory]
    [InlineData("",20,false)]
    [InlineData("PDA-01",20,true)]
    [InlineData("12345678901234567890",20,true)]
    [InlineData("123456789012345678901",20,false)]
    [InlineData("가나다라마바사아자차",20,true)]
    [InlineData("가나다라마바사아자차카",20,false)]
    [InlineData("PDA\n",20,false)]
    public void Checks_storage_bytes_not_only_characters(string value,int width,bool valid)
        => Assert.Equal(valid,LoginInput.Valid(value,width));

    [Fact]
    public void Context_must_fit_session_columns()
    {
        Assert.True(LoginInput.ValidContext("PDA","01","DAY"));
        Assert.False(LoginInput.ValidContext("PDA","","DAY"));
        Assert.False(LoginInput.ValidContext("PDA","01","12345678901"));
        Assert.True(PdaScenarioUsers.IsTestAccount("PTEST"));
        Assert.False(PdaScenarioUsers.IsAny("PTEST")); // must not enable the scenario UI
    }
}
