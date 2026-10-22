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
        Assert.True(LoginInput.ValidContext("PDA",null,null));
        Assert.True(LoginInput.ValidContext("PDA","",""));
        Assert.False(LoginInput.ValidContext("PDA"," ","DAY"));
        Assert.False(LoginInput.ValidContext("PDA",new string('L',21),null));
        Assert.False(LoginInput.ValidContext("PDA","01","12345678901"));
        Assert.False(LoginInput.ValidContext("PDA",null,"A\n"));
        Assert.False(LoginInput.ValidContext("",null,null));
        Assert.False(PdaScenarioUsers.IsTestAccount("PTEST"));
        Assert.False(PdaScenarioUsers.IsAny("PTEST")); // must not enable the scenario UI
    }

    [Theory]
    [InlineData("SCTEST1", true)]
    [InlineData("sctest2", true)]
    [InlineData("PTEST", false)]
    [InlineData("ptest", false)]
    [InlineData("EMP001", false)]
    [InlineData(null, false)]
    public void Only_seeded_scenario_accounts_are_restricted(string? employee, bool restricted)
        => Assert.Equal(restricted, PdaScenarioUsers.IsTestAccount(employee));
}
