using Microsoft.Extensions.AI;

namespace SqlDatabaseVectorSearch.Models;

public record class TokenUsageResponse(UsageDetails? Reformulation, UsageDetails? Question);
