// Licensed under the Apache License, Version 2.0 (the "License");

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGooeyBot(builder.Configuration);

builder.Logging.AddSimpleConsole(options =>
{
    options.TimestampFormat = "HH:mm:ss ";
});

var app = builder.Build();

app.Run();
