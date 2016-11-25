<%@ Page Language="C#" %>
<%@ Import Namespace="NuGet" %>
<%@ Import Namespace="NuGet.Server" %>
<%@ Import Namespace="NuGet.Server.DataServices" %>
<%@ Import Namespace="NuGet.Server.Infrastructure" %>
<!DOCTYPE html PUBLIC "-//W3C//DTD XHTML 1.0 Transitional//EN" "http://www.w3.org/TR/xhtml1/DTD/xhtml1-transitional.dtd">
<%
    var service = new Packages();
    var packages = service.Search(String.Empty, null, true, false).Where(x=>x.Listed).GroupBy(x => new {x.Id}).OrderBy(x=>x.First().Title).Take(50).ToArray();
%>
<html xmlns="http://www.w3.org/1999/xhtml">
<head id="Head1" runat="server">
    <meta charset="utf-8">
    <meta http-equiv="X-UA-Compatible" content="IE=edge">
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <title>ahd NuGet Repository</title>
    <link rel="stylesheet" href="https://maxcdn.bootstrapcdn.com/bootstrap/3.3.7/css/bootstrap.min.css" integrity="sha384-BVYiiSIFeK1dGmJRAkycuHAHRg32OmUcww7on3RYdg4Va+PmSTsz/K68vbdEjh4u" crossorigin="anonymous">
    <!-- HTML5 shim and Respond.js for IE8 support of HTML5 elements and media queries -->
    <!-- WARNING: Respond.js doesn't work if you view the page via file:// -->
    <!--[if lt IE 9]>
      <script src="https://oss.maxcdn.com/html5shiv/3.7.3/html5shiv.min.js"></script>
      <script src="https://oss.maxcdn.com/respond/1.4.2/respond.min.js"></script>
    <![endif]-->
    <style type="text/css">
        body {
          padding-top: 50px;
        }
        .sub-header {
          padding-bottom: 10px;
          border-bottom: 1px solid #eee;
        }
        .navbar-fixed-top {
          border: 0;
        }
        .sidebar {
          display: none;
        }
        @media (min-width: 768px) {
          .sidebar {
            position: fixed;
            top: 51px;
            bottom: 0;
            left: 0;
            z-index: 1000;
            display: block;
            padding: 20px;
            overflow-x: hidden;
            overflow-y: auto; /* Scrollable contents if viewport is shorter than content. */
            background-color: #f5f5f5;
            border-right: 1px solid #eee;
          }
        }
        .nav-sidebar {
          margin-right: -21px; /* 20px padding + 1px border */
          margin-bottom: 20px;
          margin-left: -20px;
        }
        .nav-sidebar > li > a {
          padding-right: 20px;
          padding-left: 20px;
        }
        .nav-sidebar > .active > a,
        .nav-sidebar > .active > a:hover,
        .nav-sidebar > .active > a:focus {
          color: #fff;
          background-color: #428bca;
        }
        .main {
          padding: 20px;
        }
        @media (min-width: 768px) {
          .main {
            padding-right: 40px;
            padding-left: 40px;
          }
        }
        .main .page-header {
          margin-top: 0;
        }
        .placeholders {
          margin-bottom: 30px;
          text-align: center;
        }
        .placeholders h4 {
          margin-bottom: 0;
        }
        .placeholder {
          margin-bottom: 20px;
        }
        .placeholder img {
            display: inline-block;
            border-radius: 50%;
        }
        div.panel tr.prerelease {
            font-style: italic;
        }
        div.panel tr.latest {
            font-weight: bold;
        }
    </style>
</head>
<body>
    <nav class="navbar navbar-inverse navbar-fixed-top">
      <div class="container">
        <div class="navbar-header">
          <button type="button" class="navbar-toggle collapsed" data-toggle="collapse" data-target="#navbar" aria-expanded="false" aria-controls="navbar">
            <span class="sr-only">Toggle navigation</span>
            <span class="icon-bar"></span>
            <span class="icon-bar"></span>
            <span class="icon-bar"></span>
          </button>
          <a class="navbar-brand" href="/">ahd Nuget Server</a>
        </div>
        <div id="navbar" class="collapse navbar-collapse">
          <ul class="nav navbar-nav">
            <li><a href="<%= VirtualPathUtility.ToAbsolute("~/nuget/Packages") %>">Feed</a></li>
            <% if (Request.IsLocal) { %>
              <li><a href="<%= VirtualPathUtility.ToAbsolute("~/nugetserver/api/clear-cache") %>">Clear Cache</a></li>
            <% } %>
          </ul>
        </div><!--/.nav-collapse -->
      </div>
    </nav>
     <div class="container">
      <div class="row">
        <div class="col-sm-3 col-md-2 sidebar">
          <ul class="nav nav-sidebar">
            <li class="active"><a href="#">Overview <span class="sr-only">(current)</span></a></li>
          </ul>
          <ul class="nav nav-sidebar">
              <%foreach (var package in packages)
                {
                    var first = package.First();%>
                 <li><a href="#<%= package.Key.Id.Replace(' ', '_')%>"><%= first.Title %></a></li>     
              <%}%>
          </ul>
        </div>
        <div class="col-sm-9 col-sm-offset-3 col-md-10 col-md-offset-2 main">
            <h1>ahd Nuget Server</h1>
            <div>
                <h2>You are running NuGet.Server v<%= typeof(NuGet.Server.DataServices.ODataPackage).Assembly.GetName().Version %></h2>
                <fieldset style="width:800px">
                    <legend><strong>Repository URLs</strong></legend>
                    In the package manager settings, add the following URL to the list of 
                    Package Sources:
                    <blockquote>
                        <strong><%= Helpers.GetRepositoryUrl(Request.Url, Request.ApplicationPath) %></strong>
                    </blockquote>
                    <% if (string.IsNullOrEmpty(ConfigurationManager.AppSettings["apiKey"])) { %>
                    To enable pushing packages to this feed using the <a href="https://www.nuget.org/downloads">NuGet command line tool</a> (nuget.exe), set the api key appSetting in web.config.
                    <% } else { %>
                    Use the command below to push packages to this feed using the <a href="https://www.nuget.org/downloads">NuGet command line tool</a> (nuget.exe).
                    <% } %>
                    <blockquote>
                        <strong>nuget.exe push {package file} {apikey} -Source <%= Helpers.GetPushUrl(Request.Url, Request.ApplicationPath) %></strong>
                    </blockquote>            
                </fieldset>

                <% if (Request.IsLocal) { %>
                <fieldset style="width:800px">
                    <legend><strong>Adding packages</strong></legend>

                    To add packages to the feed put package files (.nupkg files) in the folder
                    <code><% = PackageUtility.PackagePhysicalPath %></code><br/><br/>
                </fieldset>
                <% } %>
                <% foreach (var package in packages)
                   {
                       var stableOnly = false;
                       var versions = package.OrderByDescending(x => SemanticVersion.Parse(x.Version));
                       var meta = versions.FirstOrDefault(x=>x.IsLatestVersion);
                       if (meta == null)
                            meta = versions.First();%>
                <div class="panel panel-default" id="<%= meta.Id %>">
                  <div class="panel-heading">
                    <h3 class="panel-title"><%= meta.Title%></h3>
                  </div>
                  <div class="panel-body">
                    <p><%= meta.Description %></p>
                    <dl>
                        <% if (!String.IsNullOrEmpty(meta.LicenseUrl)){ %>
                        <dt>License:</dt>
                        <dd><a href="<%= meta.LicenseUrl %>" target="_blank"><%= meta.LicenseUrl %></a></dd>
                        <% }
                           if (!String.IsNullOrEmpty(meta.ProjectUrl)){ %>
                        <dt>Project Url:</dt>
                        <dd><a href="<%= meta.ProjectUrl %>" target="_blank"><%=meta.ProjectUrl %></a></dd>
                         <% }%>
                        <dt>Author:</dt>
                        <dd><%= meta.Authors %></dd>
                    </dl>
                    <h4><% foreach (var tag in meta.Tags.Split(' ', ',')) { %>
                       <span class="label label-default"><%= tag %></span>
                    <%} %></h4>
                    <%  var dsp = new DataServicePackage();
                        dsp.Dependencies = meta.Dependencies;
                        if (dsp.DependencySets.Any())
                        {%>
                      <h4>Dependencies:</h4>
                      <dl>
                     <% }
                         foreach (var set in dsp.DependencySets)
                         {
                             if (set.Dependencies.Count == 0) continue; %>
                            <dt><%= set.TargetFramework %></dt>
                    <%
                             foreach (var dep in set.Dependencies)
                             { %>
                                <dd><%=dep.ToString() %></dd>
                         <%  }
                         }
                         if (dsp.DependencySets.Any()) {%>

                      </dl>
                     <% }%>
                  </div>
                    <table class="table">
                        <thead>
                        <tr>
                            <th>Version</th>
                            <th>Date</th>
                        </tr>
                        </thead>
                        <tbody>
                          <% foreach (var version in versions)
                             {
                                 if (!version.IsPrerelease) stableOnly = true;
                                 if (stableOnly && version.IsPrerelease) continue;
                                 var css = String.Empty;
                                 if (version.IsLatestVersion) css = "latest";
                                 if (version.IsPrerelease) css = "prerelease";
                                  %>
                            <tr class="<%=css%>">
                              <td><%= version.Version %></td>
                              <td><%= version.LastUpdated %></td>
                            </tr>
                          <%} %>
                        </tbody>
                    </table>
                </div>       
                <%} %>
            </div>
        </div>
      </div>
    </div><!-- /.container -->
    <script src="https://ajax.googleapis.com/ajax/libs/jquery/1.12.4/jquery.min.js"></script>
</body>
</html>
