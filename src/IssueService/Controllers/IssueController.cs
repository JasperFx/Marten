using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Marten;
using Marten.AspNetCore;
using Marten.Linq;
using Microsoft.AspNetCore.Mvc;

namespace IssueService.Controllers
{
    public class Note
    {
        public string UserName { get; set; }
        public DateTime Timestamp { get; set; }
        public string Text { get; set; }
    }

    public class Issue
    {
        public static Issue RandomIssue()
        {
            return new Issue {Description = Guid.NewGuid().ToString(), Open = Random.Shared.Next(0, 10) > 7};
        }

        public Guid Id { get; set; }
        public string Description { get; set; }
        public bool Open { get; set; }

        public IList<Note> Notes { get; set; }
    }


    public class GetIssueController: ControllerBase
    {
        #region sample_write_single_document_by_id_to_httpresponse

        [HttpGet("/issue/{issueId}")]
        public Task Get(Guid issueId, [FromServices] IQuerySession session, [FromQuery] string? sc = null)
        {
            // This "streams" the raw JSON to the HttpResponse
            // w/o ever having to read the full JSON string or
            // deserialize/serialize within the HTTP request
            return sc is null
                ? session.Json
                    .WriteById<Issue>(issueId, HttpContext)
                : session.Json
                    .WriteById<Issue>(issueId, HttpContext, onFoundStatus: int.Parse(sc));

        }

        #endregion

        #region sample_use_linq_to_write_single_document_to_httpcontext

        [HttpGet("/issue2/{issueId}")]
        public Task Get2(Guid issueId, [FromServices] IQuerySession session, [FromQuery] string? sc = null)
        {
            return sc is null
                ? session.Query<Issue>().Where(x => x.Id == issueId)
                    .WriteSingle(HttpContext)
                : session.Query<Issue>().Where(x => x.Id == issueId)
                    .WriteSingle(HttpContext, onFoundStatus: int.Parse(sc));
        }

        #endregion

        #region sample_write_single_document_to_httpcontext_with_compiled_query

        [HttpGet("/issue3/{issueId}")]
        public Task Get3(Guid issueId, [FromServices] IQuerySession session, [FromQuery] string? sc = null)
        {
            return sc is null
                ? session.Query<Issue>(new IssueById() { Id = issueId })
                    .WriteSingle(HttpContext)
                : session.Query<Issue>(new IssueById() { Id = issueId })
                    .WriteSingle(HttpContext, onFoundStatus: int.Parse(sc));
        }

        #endregion


        #region sample_writing_multiple_documents_to_httpcontext

        [HttpGet("/issue/open")]
        public Task OpenIssues([FromServices] IQuerySession session, [FromQuery] string? sc = null)
        {
            // This "streams" the raw JSON to the HttpResponse
            // w/o ever having to read the full JSON string or
            // deserialize/serialize within the HTTP request
            return sc is null
                ? session.Query<Issue>().Where(x => x.Open)
                    .WriteArray(HttpContext)
                : session.Query<Issue>().Where(x => x.Open)
                    .WriteArray(HttpContext, onFoundStatus: int.Parse(sc));
        }

        #endregion

        #region sample_using_compiled_query_with_json_streaming

        [HttpGet("/issue2/open")]
        public Task OpenIssues2([FromServices] IQuerySession session, [FromQuery] string? sc = null)
        {
            return sc is null
                ? session.WriteArray(new OpenIssues(), HttpContext)
                : session.WriteArray(new OpenIssues(), HttpContext, onFoundStatus: int.Parse(sc));
        }

        #endregion
    }

    #region sample_OpenIssues

    public class OpenIssues: ICompiledListQuery<Issue>
    {
        public Expression<Func<IMartenQueryable<Issue>, IEnumerable<Issue>>> QueryIs()
        {
            return q => q.Where(x => x.Open);
        }
    }

    #endregion

    #region sample_IssueById

    public class IssueById: ICompiledQuery<Issue, Issue>
    {
        public Expression<Func<IMartenQueryable<Issue>, Issue>> QueryIs()
        {
            return q => q.FirstOrDefault(x => x.Id == Id);
        }

        public Guid Id { get; set; }
    }

    #endregion
}
