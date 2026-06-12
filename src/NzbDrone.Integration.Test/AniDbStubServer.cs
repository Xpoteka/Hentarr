using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NzbDrone.Integration.Test
{
    // Serves a tiny fake AniDB (titles dump + HTTP API) so integration tests
    // can exercise the lookup/add flow without touching the real AniDB.
    public class AniDbStubServer : IDisposable
    {
        private const string TitlesXml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<animetitles>
  <anime aid=""77"">
    <title xml:lang=""x-jat"" type=""main"">My Family Pies</title>
  </anime>
  <anime aid=""126"">
    <title xml:lang=""x-jat"" type=""main"">BrattySis</title>
  </anime>
  <anime aid=""350"">
    <title xml:lang=""x-jat"" type=""main"">5K Porn</title>
  </anime>
  <anime aid=""351"">
    <title xml:lang=""x-jat"" type=""main"">Bratty Sis</title>
  </anime>
</animetitles>";

        private static int _portCounter = 8745;

        private readonly HttpListener _listener;

        public string ApiUrl { get; }
        public string TitlesUrl { get; }

        public AniDbStubServer()
        {
            var port = Interlocked.Increment(ref _portCounter);
            var prefix = string.Format("http://127.0.0.1:{0}/anidb/", port);

            ApiUrl = prefix + "httpapi";
            TitlesUrl = prefix + "anime-titles.xml.gz";

            _listener = new HttpListener();
            _listener.Prefixes.Add(prefix);
            _listener.Start();

            Task.Run(Listen);
        }

        public void Dispose()
        {
            try
            {
                _listener.Stop();
                _listener.Close();
            }
            catch
            {
                // Listener might already be stopped, nothing to clean up.
            }
        }

        private void Listen()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context;

                try
                {
                    context = _listener.GetContext();
                }
                catch
                {
                    // Listener was stopped.
                    return;
                }

                try
                {
                    Handle(context);
                }
                catch
                {
                    // Ignore failures writing a response, the test will surface the problem.
                }
            }
        }

        private void Handle(HttpListenerContext context)
        {
            var path = context.Request.Url.AbsolutePath;

            if (path.EndsWith("anime-titles.xml.gz", StringComparison.OrdinalIgnoreCase))
            {
                WriteGzip(context.Response, TitlesXml);

                return;
            }

            if (path.EndsWith("httpapi", StringComparison.OrdinalIgnoreCase))
            {
                var aid = context.Request.QueryString["aid"];

                WriteText(context.Response, GetAnimeXml(aid) ?? "<error>No such anime</error>");

                return;
            }

            context.Response.StatusCode = 404;
            context.Response.Close();
        }

        private static void WriteText(HttpListenerResponse response, string content)
        {
            var bytes = Encoding.UTF8.GetBytes(content);

            response.ContentType = "text/xml";
            response.ContentLength64 = bytes.Length;
            response.OutputStream.Write(bytes, 0, bytes.Length);
            response.Close();
        }

        private static void WriteGzip(HttpListenerResponse response, string content)
        {
            byte[] bytes;

            using (var memory = new MemoryStream())
            {
                using (var gzip = new GZipStream(memory, CompressionMode.Compress, true))
                {
                    var raw = Encoding.UTF8.GetBytes(content);
                    gzip.Write(raw, 0, raw.Length);
                }

                bytes = memory.ToArray();
            }

            response.ContentType = "application/gzip";
            response.ContentLength64 = bytes.Length;
            response.OutputStream.Write(bytes, 0, bytes.Length);
            response.Close();
        }

        private static string GetAnimeXml(string aid)
        {
            switch (aid)
            {
                case "77":
                    return @"<anime id=""77"" restricted=""true"">
  <type>TV Series</type>
  <episodecount>3</episodecount>
  <startdate>2018-11-30</startdate>
  <titles>
    <title xml:lang=""x-jat"" type=""main"">My Family Pies</title>
  </titles>
  <creators>
    <name id=""1"" type=""Animation Work"">Fake Studio</name>
  </creators>
  <description>Integration test anime.</description>
  <ratings>
    <permanent count=""10"">7.50</permanent>
  </ratings>
  <episodes>
    <episode id=""7701""><epno type=""1"">1</epno><length>25</length><airdate>2018-11-30</airdate><title xml:lang=""en"">Pilot</title></episode>
    <episode id=""7702""><epno type=""1"">2</epno><length>25</length><airdate>2018-12-30</airdate><title xml:lang=""en"">Home From College - S6:E1</title></episode>
    <episode id=""7703""><epno type=""1"">3</epno><length>25</length><airdate>2019-01-30</airdate><title xml:lang=""en"">Third Episode</title></episode>
  </episodes>
</anime>";

                case "126":
                    return @"<anime id=""126"" restricted=""true"">
  <type>TV Series</type>
  <episodecount>2</episodecount>
  <startdate>2017-06-01</startdate>
  <titles>
    <title xml:lang=""x-jat"" type=""main"">BrattySis</title>
  </titles>
  <creators>
    <name id=""1"" type=""Animation Work"">Fake Studio</name>
  </creators>
  <description>Integration test anime.</description>
  <ratings>
    <permanent count=""10"">7.00</permanent>
  </ratings>
  <episodes>
    <episode id=""12601""><epno type=""1"">1</epno><length>25</length><airdate>2017-06-01</airdate><title xml:lang=""en"">First</title></episode>
    <episode id=""12602""><epno type=""1"">2</epno><length>25</length><airdate>2017-07-01</airdate><title xml:lang=""en"">Second</title></episode>
  </episodes>
</anime>";

                case "350":
                    return @"<anime id=""350"" restricted=""true"">
  <type>TV Series</type>
  <episodecount>2</episodecount>
  <startdate>2019-02-01</startdate>
  <titles>
    <title xml:lang=""x-jat"" type=""main"">5K Porn</title>
  </titles>
  <creators>
    <name id=""1"" type=""Animation Work"">Other Fake Studio</name>
  </creators>
  <description>Integration test anime.</description>
  <ratings>
    <permanent count=""10"">6.50</permanent>
  </ratings>
  <episodes>
    <episode id=""35001""><epno type=""1"">1</epno><length>25</length><airdate>2019-02-01</airdate><title xml:lang=""en"">First</title></episode>
    <episode id=""35002""><epno type=""1"">2</epno><length>25</length><airdate>2019-03-01</airdate><title xml:lang=""en"">Second</title></episode>
  </episodes>
</anime>";

                case "351":
                    return @"<anime id=""351"" restricted=""true"">
  <type>TV Series</type>
  <episodecount>2</episodecount>
  <startdate>2017-01-05</startdate>
  <titles>
    <title xml:lang=""x-jat"" type=""main"">Bratty Sis</title>
  </titles>
  <creators>
    <name id=""1"" type=""Animation Work"">Other Fake Studio</name>
  </creators>
  <description>Integration test anime.</description>
  <ratings>
    <permanent count=""10"">6.00</permanent>
  </ratings>
  <episodes>
    <episode id=""35101""><epno type=""1"">1</epno><length>25</length><airdate>2017-01-05</airdate><title xml:lang=""en"">First</title></episode>
    <episode id=""35102""><epno type=""1"">2</epno><length>25</length><airdate>2017-02-05</airdate><title xml:lang=""en"">Second</title></episode>
  </episodes>
</anime>";

                default:
                    return null;
            }
        }
    }
}
