namespace PowerP.Realtime.API.Client.DTO;
using System;
public class DatabaseDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int ConnectionId { get; set; }
    public int PowerPlantId { get; set; }
}