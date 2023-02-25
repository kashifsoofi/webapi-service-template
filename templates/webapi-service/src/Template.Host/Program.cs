﻿using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.SimpleNotificationService;
using Amazon.SQS;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NServiceBus;
using Serilog;
using Serilog.Events;
using Template.Host;
using Template.Infrastructure.Configuration;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("Template.Host.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();

IConfiguration configuration = new ConfigurationBuilder()
              .AddJsonFile("appsettings.json", true, true)
              .AddEnvironmentVariables()
              .Build();

var host = CreateHostBuilder(args)
    .UseSerilog()
    .ConfigureServices((hostBuilderContext, services) =>
    {
        services.AddOptions();
        services.Configure<NServiceBusOptions>(configuration.GetSection("NServiceBus"));
    })
    .ConfigureContainer((HostBuilderContext hostBuilderContext, ContainerBuilder builder) =>
    {
        var startup = new Startup(hostBuilderContext.Configuration);
        startup.ConfigureContainer(builder);
    })
    .UseNServiceBus(context =>
    {
        var nServiceBusOptions = configuration.GetSection("NServiceBus").Get<NServiceBusOptions>();

        var endpointConfiguration = new EndpointConfiguration("Template.Host");
        endpointConfiguration.DoNotCreateQueues();

        var regionEndpoint = RegionEndpoint.GetBySystemName("eu-west-1");

        var amazonSqsConfig = new AmazonSQSConfig();
        amazonSqsConfig.RegionEndpoint = regionEndpoint;
        if (!string.IsNullOrEmpty(nServiceBusOptions.SqsServiceUrlOverride))
        {
            amazonSqsConfig.ServiceURL = nServiceBusOptions.SqsServiceUrlOverride;
        }

        var transport = endpointConfiguration.UseTransport<SqsTransport>();
        transport.ClientFactory(() => new AmazonSQSClient(
            new AnonymousAWSCredentials(),
            amazonSqsConfig));

        var amazonSimpleNotificationServiceConfig = new AmazonSimpleNotificationServiceConfig();
        amazonSimpleNotificationServiceConfig.RegionEndpoint = regionEndpoint;
        if (!string.IsNullOrEmpty(nServiceBusOptions.SnsServiceUrlOverride))
        {
            amazonSimpleNotificationServiceConfig.ServiceURL = nServiceBusOptions.SnsServiceUrlOverride;
        }

        transport.ClientFactory(() => new AmazonSimpleNotificationServiceClient(
            new AnonymousAWSCredentials(),
            amazonSimpleNotificationServiceConfig));

        var amazonS3Config = new AmazonS3Config
        {
            ForcePathStyle = true,
        };
        if (!string.IsNullOrEmpty(nServiceBusOptions.S3ServiceUrlOverride))
        {
            amazonS3Config.ServiceURL = nServiceBusOptions.S3ServiceUrlOverride;
        }

        var s3Configuration = transport.S3("Template", "api");
        s3Configuration.ClientFactory(() => new AmazonS3Client(
            new AnonymousAWSCredentials(),
            amazonS3Config));

        endpointConfiguration.SendFailedMessagesTo("error");
        endpointConfiguration.EnableInstallers();

        return endpointConfiguration;
    })
    .UseConsoleLifetime();

await host.RunConsoleAsync();

static IHostBuilder CreateHostBuilder(string[] args) =>
    Host.CreateDefaultBuilder(args)
        .UseServiceProviderFactory(new AutofacServiceProviderFactory());