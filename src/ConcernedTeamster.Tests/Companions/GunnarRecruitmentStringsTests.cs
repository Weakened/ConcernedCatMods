using TheConcernedCat.ConcernedTeamster.Domain.Companions;
namespace TheConcernedCat.ConcernedTeamster.Tests.Companions;
public sealed class GunnarRecruitmentStringsTests
{
    [Fact] public void NewPlayerCopySupportsOverridesAndRoundTripSafeFallback()
    {
        foreach(var row in GunnarRecruitmentStrings.English)
        {
            Assert.Equal(row.Value.Trim(),row.Value);
            Assert.DoesNotContain('\t',row.Value); Assert.DoesNotContain('\n',row.Value);
            Assert.Equal(row.Value,GunnarRecruitmentStrings.Resolve(row.Key,key=>key));
            Assert.Equal("translated",GunnarRecruitmentStrings.Resolve(row.Key,_=>"translated"));
        }
        Assert.Equal("Use Cart",string.Format(GunnarRecruitmentStrings.Resolve("gunnar.recruit.question",_=>"Use {0}"),"Cart"));
    }
}
