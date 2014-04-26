﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Nustache.Core;
using OwinExtensions;

namespace qed
{
    using MiddlewareFunc = Func<Func<IDictionary<string, object>, Task>, Func<IDictionary<string, object>, Task>>;

    public static class Mustache
    {
        class MustacheConfiguration
        {
            public Func<IDictionary<string, object>, object> LayoutDataFunc { get; set; }
            public string LayoutTemplateName { get; set; }
            public string TemplateFileExtension { get; set; }
            public string TemplateRootPath { get; set; }
        }

        const string _confiugrationKey = "owinmustache.Confguration";

        public static MiddlewareFunc Create(
            string templateRootDirectoryName = null,
            string templateFileExtension = null,
            string layoutTemplateName = null,
            Func<IDictionary<string, object>, object> layoutDataFunc = null)
        {
            templateRootDirectoryName = templateRootDirectoryName ?? "templates";
            templateFileExtension = templateFileExtension ?? ".mustache";
            layoutTemplateName = layoutTemplateName ?? "_layout";

            var templateRootPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, templateRootDirectoryName);

            var configuration = new MustacheConfiguration
            {
                TemplateRootPath = templateRootPath,
                TemplateFileExtension = templateFileExtension,
                LayoutTemplateName = layoutTemplateName,
                LayoutDataFunc = layoutDataFunc
            };

            return next => environment =>
            {
                environment[_confiugrationKey] = configuration;

                return next(environment);
            };
        }

        private static object GetLayoutData(
            MustacheConfiguration configuration,
            IDictionary<string, object> environment,
            object data)
        {
            if (configuration.LayoutDataFunc == null)
                return data;

            var templateData = data.ToDictionary();
            var layoutData = configuration.LayoutDataFunc(environment).ToDictionary();

            return new[] { templateData, layoutData }
                .SelectMany(x => x)
                .ToDictionary(pair => pair.Key, pair => pair.Value);
        }

        static Template GetTemplate(string templateName)
        {
            var templateSource = ReadEmbeddedTemplate(templateName);

            var template = new Template();
            template.Load(new StringReader(templateSource));
            return template;
        }

        static bool HasLayout(MustacheConfiguration configuration)
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = MakeEmbeddedTemplateResourceName(configuration.LayoutTemplateName);

            return assembly.GetManifestResourceNames().Contains(resourceName);
        }

        static string MakeEmbeddedTemplateResourceName(string templateName)
        {
            return String.Concat("qed.MustacheTemplates.", templateName, ".mustache");
        }

        static string ReadEmbeddedTemplate(string templateName)
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = MakeEmbeddedTemplateResourceName(templateName);

            using (var stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                    throw new InvalidOperationException(String.Format("No embedded template resource named {0} exists.", templateName));

                using (var reader = new StreamReader(stream))
                {
                    return reader.ReadToEnd();
                }
            }
        }

        public static Task Render(
            this IDictionary<string, object> environment,
            string templateName,
            object data)
        {
            var configuration = environment[_confiugrationKey] as MustacheConfiguration;
            if (configuration == null)
                throw new InvalidOperationException("The OwinMustache middleware is not in use.");

            var responseHeaders = environment.GetResponseHeaders();
            if (responseHeaders == null)
                throw new InvalidOperationException("The OWIN environment did not have response headers.");
            responseHeaders["Content-Type"] = new[] {"text/html"};

            var responseBody = environment.GetResponseBody();
            if (responseBody == null)
                throw new InvalidOperationException("The OWIN environment did not have a response stream.");

            if (HasLayout(configuration) && !templateName.Equals(configuration.LayoutTemplateName))
            {
                var layout = GetTemplate(configuration.LayoutTemplateName);
                var layoutData = GetLayoutData(configuration, environment, data);
                return RenderTemplate(responseBody, layout, layoutData, hasLayout: true,  bodyTemplateName: templateName);
            }

            var template = GetTemplate(templateName);
            return RenderTemplate(responseBody, template, data, hasLayout: false);
        }

        static Task RenderTemplate(
            Stream responseStream,
            Template template, 
            object data,
            bool hasLayout,
            string bodyTemplateName = null)
        {
            return Task.Run(() =>
            {
                using (var writer = new StreamWriter(responseStream, Encoding.UTF8, 1, true))
                {
                    template.Render(
                        data,
                        writer,
                        name =>
                        {
                            if (hasLayout && name.Equals("body", StringComparison.OrdinalIgnoreCase))
                                return GetTemplate(bodyTemplateName);

                            return GetTemplate(name);
                        });
                }
            });
        }
    }
}
