using Gram.Blazor.Shared;
using Xunit;

namespace Gram.Blazor.UnitTests;

public sealed class SummaryEvaluationCorpusTests
{
    [Fact]
    public void Corpus_HasEightSourcesAndTenVariedQuestions()
    {
        Assert.Equal(8, SummaryEvaluationCorpus.Sources.Count);
        Assert.Equal(10, SummaryEvaluationCorpus.Questions.Count);
        Assert.Contains(SummaryEvaluationCorpus.Questions, question => question.Skill == "cross-source reasoning");
        Assert.Contains(SummaryEvaluationCorpus.Questions, question => question.Skill == "calculation");
        Assert.Contains(SummaryEvaluationCorpus.Questions, question => question.Skill == "limitation detection");
    }
}
