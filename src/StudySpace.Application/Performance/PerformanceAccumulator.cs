using StudySpace.Application.Projects;
using StudySpace.Application.Quizzes;
using StudySpace.Domain.Common;
namespace StudySpace.Application.Performance;

// Inputs arrive newest completed attempt first; windows retain bounded evidence across distinct attempts.
public sealed class PerformanceAccumulator
{
    private readonly Dictionary<(PerformanceGroupKind,string),Group> groups=[];
    private readonly List<HistoryAttempt> recent=[];
    private int completed,demo;private double totalPercentage;
    public void Add(QuizSession session)
    {
        if(session.Attempt.Status!=AttemptStatus.Completed||!session.Attempt.Score.HasValue)return;
        completed++;if(session.IsMock)demo++;
        totalPercentage+=100*session.Attempt.Score.Value/session.Questions.Count;
        if(recent.Count<10)recent.Add(StudyPerformanceService.Summarize(session));
        foreach(var q in session.Questions.OrderByDescending(q=>q.Position))
        {
            var answer=session.Answers.Single(a=>a.QuestionId==q.Id);
            var measures=new List<Measure>();
            var criteria=q.Kind==QuestionKind.Open?q.Rubric:[];
            if(criteria.Count>0)
            {
                foreach(var criterion in criteria)
                {
                    var grade=answer.Feedback?.Criteria.SingleOrDefault(c=>c.CriterionId==criterion.Id)?.Grade??answer.Feedback?.Score??0;
                    var citations=q.Citations.Where(c=>criterion.ChunkIds.Contains(c.ChunkId)).ToArray();
                    measures.Add(new(PerformanceGroupKind.Concept,ProjectText.SearchKey(criterion.Concept),criterion.Concept,criterion.Weight,criterion.Weight*grade,citations));
                    AddDimensions(measures,q,citations,criterion.Weight,grade);
                }
            }
            else AddDimensions(measures,q,q.Citations,1,answer.Feedback?.Score??0);
            foreach(var measure in measures.GroupBy(m=>(m.Kind,m.Key)))
            {
                var first=measure.First();var key=(first.Kind,first.Key);
                if(!groups.TryGetValue(key,out var g))groups[key]=g=new(first.Kind,first.Key,first.Name);
                var weight=measure.Sum(m=>m.Weight);var earned=measure.Sum(m=>m.Earned);
                g.Observe(answer.IsAnswered,weight,earned,session.Attempt.Id,q.Id,
                    measure.SelectMany(m=>m.Citations).DistinctBy(c=>c.ChunkId).ToArray(),answer.Feedback);
            }
        }
    }
    private static void AddDimensions(List<Measure> output,QuizQuestionSnapshot q,IReadOnlyList<StudySpace.Domain.Sources.SourceCitation> citations,double weight,double grade)
    {
        var sources=citations.GroupBy(c=>c.SourceId).ToArray();
        foreach(var source in sources)output.Add(new(PerformanceGroupKind.Source,source.Key.ToString("D"),source.First().SourceTitle,weight/sources.Length,weight*grade/sources.Length,source.ToArray()));
        var topics=(q.Topics??[]).Where(t=>citations.Any(c=>t.ChunkIds.Contains(c.ChunkId))).DistinctBy(t=>ProjectText.SearchKey(t.Name)).ToArray();
        foreach(var topic in topics)output.Add(new(PerformanceGroupKind.Topic,ProjectText.SearchKey(topic.Name),topic.Name,weight/topics.Length,weight*grade/topics.Length,citations.Where(c=>topic.ChunkIds.Contains(c.ChunkId)).ToArray()));
        var tags=(q.Tags??[]).Where(t=>citations.Any(c=>t.ChunkIds.Contains(c.ChunkId))).DistinctBy(t=>t.Id).ToArray();
        foreach(var tag in tags)output.Add(new(PerformanceGroupKind.Tag,tag.Id.ToString("D"),tag.Name,weight/tags.Length,weight*grade/tags.Length,citations.Where(c=>tag.ChunkIds.Contains(c.ChunkId)).ToArray()));
    }
    public ProjectPerformance Build()
    {
        var all=groups.Values.Select(g=>g.Build()).ToArray();
        return new(completed,completed==0?null:totalPercentage/completed,recent.ToArray(),
            all.Where(g=>g.EvidenceCount>0&&g.DifficultyIndex>=0.3-1e-9).OrderByDescending(g=>g.DifficultyIndex).ThenByDescending(g=>g.EvidenceCount).ThenBy(g=>g.Name,StringComparer.Ordinal).ToArray(),
            all.Where(g=>g.Improvement>=0.1-1e-9).OrderByDescending(g=>g.Improvement).ThenBy(g=>g.Name,StringComparer.Ordinal).ToArray(),demo);
    }
    private sealed record Measure(PerformanceGroupKind Kind,string Key,string Name,double Weight,double Earned,IReadOnlyList<StudySpace.Domain.Sources.SourceCitation> Citations);
    private sealed class Group(PerformanceGroupKind kind,string key,string name)
    {
        private int count,unanswered;private double weight,earned;
        private readonly List<(double Score,double Weight)> window=[],latestPeriod=[],previousPeriod=[];
        private Guid? latestAttempt;
        private readonly List<PerformanceReference> references=[];
        private readonly HashSet<string> hints=new(StringComparer.Ordinal);
        public void Observe(bool answered,double possible,double points,Guid attempt,Guid question,IReadOnlyList<StudySpace.Domain.Sources.SourceCitation> citations,AnswerFeedback? feedback)
        {
            if(!answered){unanswered++;return;}
            count++;weight+=possible;earned+=points;
            var observation=(points/possible,possible);
            if(window.Count<5)window.Add(observation);
            latestAttempt??=attempt;
            if(attempt==latestAttempt){if(latestPeriod.Count<5)latestPeriod.Add(observation);}
            else if(previousPeriod.Count<5)previousPeriod.Add(observation);
            if(points/possible>=0.8)return;
            foreach(var citation in citations)
                if(references.Count<3&&!references.Any(r=>r.Citation.ChunkId==citation.ChunkId))references.Add(new(attempt,question,citation));
            if(hints.Count<3&&feedback is not null)
            {
                if(feedback.SemanticEvaluation is { } evaluation)
                    foreach(var suggestion in evaluation.Evaluation.Suggestions.Take(3-hints.Count))hints.Add(suggestion);
                else if(!string.IsNullOrWhiteSpace(feedback.Hint))hints.Add(feedback.Hint);
            }
        }
        public PerformanceGroup Build()
        {
            var latest=window.ToArray();
            double Mean((double Score,double Weight)[] values)=>values.Sum(v=>v.Score*v.Weight)/values.Sum(v=>v.Weight);
            double? improvement=latestPeriod.Count>=3&&previousPeriod.Count>=3?Mean(latestPeriod.ToArray())-Mean(previousPeriod.ToArray()):null;
            var suggestions=hints.ToList();
            if(references.Count>0)
            {
                var source=references[0].Citation;
                suggestions.Insert(0,$"Rivedi «{source.SourceTitle}»"+(string.IsNullOrWhiteSpace(source.Locator)?".":$", {source.Locator}."));
            }
            else suggestions.Insert(0,$"Ripassa il concetto «{name}» e prova a spiegarlo con parole tue.");
            return new(kind,key,name,count,unanswered,weight>0?Math.Clamp(1-earned/weight,0,1):0,latest.Length,
                latest.Count(v=>v.Score==0),latest.Count(v=>v.Score>0&&v.Score<0.8),improvement,suggestions.Take(3).ToArray(),references.ToArray());
        }
    }
}
