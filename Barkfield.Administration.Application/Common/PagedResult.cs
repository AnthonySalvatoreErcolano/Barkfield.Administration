using System;
using System.Collections.Generic;
using System.Text;

namespace Barkfield.Administration.Application.Common
{
    public record PagedResult<T>(IReadOnlyCollection<T> Items, int TotalCount, int PageNumber, int PageSize)
    {
        public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
        public bool HasNextPage => PageNumber < TotalPages;
        public bool HasPreviousPage => PageNumber > 1;
    }
}
