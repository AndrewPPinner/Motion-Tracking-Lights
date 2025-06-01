using OpenCvSharp;
using VideoProcessingServer.Handlers;

var builder = WebApplication.CreateBuilder(args);

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();


var webSocketOptions = new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(120)
};

app.UseWebSockets(webSocketOptions);
app.UseRouting();

using var refImg = Cv2.ImRead("C:\\Users\\andpp\\Downloads\\reference.jpg", ImreadModes.Color);

var sub = BackgroundSubtractorKNN.Create();
for (int i = 0; i < 30; i++)
    sub.Apply(refImg, new Mat(), 0.1);

app.Map("/ws/stream", async context =>
{
    if (context.WebSockets.IsWebSocketRequest)
    {
        var webSocket = await context.WebSockets.AcceptWebSocketAsync();
        await WebSocketHandler.HandleStream(webSocket, sub);
    }
    else
    {
        context.Response.StatusCode = 400;
    }
});

app.Run();
