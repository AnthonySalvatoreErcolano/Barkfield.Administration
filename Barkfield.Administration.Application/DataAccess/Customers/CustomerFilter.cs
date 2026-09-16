using System;
using System.Collections.Generic;
using System.Text;

namespace Barkfield.Administration.Application.DataAccess.Customers
{
    public record CustomerFilter(
    string? SearchTerm = null,
    string? Email = null,
    bool? HasSquareAccount = null,
    int PageNumber = 1,
    int PageSize = 20,
    string? SortBy = "CreatedAt",
    bool SortDescending = true
)
    {// Clamp page size to prevent clients from requesting 100,000 records at once
        public int PageSize { get; init; } = Math.Min(PageSize, 100);
        public int Skip => (Math.Max(PageNumber, 1) - 1) * PageSize;
    }
}
