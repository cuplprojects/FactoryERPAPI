using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ERPAPI.Model;
using ERPAPI.Data;
using ERPGenericFunctions.Model;
using System.Globalization;

namespace ERPAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ReportsController : ControllerBase
    {
        private readonly AppDbContext _context;

        public ReportsController(AppDbContext context)
        {
            _context = context;
        }



        [HttpPost("CreateReport")]
        public async Task<IActionResult> CreateReport([FromBody] Reports report)
        {
            try
            {
                if (report == null)
                {
                    return BadRequest(new { Message = "Invalid report data." });
                }

                await _context.Set<Reports>().AddAsync(report);
                await _context.SaveChangesAsync();

                return Ok(new { Message = "Report created successfully.", ReportId = report.ReportId });
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new { Message = "An error occurred while creating the report.", Details = ex.Message });
            }
        }


        // GET: api/Reports/GetAllGroups
        [HttpGet("GetAllGroups")]
        public async Task<IActionResult> GetAllGroups()
        {
            try
            {
                // Query the database for all groups and select the required fields
                var groups = await _context.Set<Group>()
                    .Select(g => new
                    {
                        g.Id,
                        g.Name,
                        g.Status
                    })
                    .ToListAsync();

                // Check if groups exist
                if (groups == null || groups.Count == 0)
                {
                    return NotFound(new { Message = "No groups found." });
                }

                return Ok(groups);
            }
            catch (Exception ex)
            {
                // Handle errors gracefully
                return StatusCode(StatusCodes.Status500InternalServerError, new { Message = "An error occurred.", Details = ex.Message });
            }
        }





        // GET: api/Reports/GetProjectsByGroupId/{groupId}
        [HttpGet("GetProjectsByGroupId/{groupId}")]
        public async Task<IActionResult> GetProjectsByGroupId(int groupId)
        {
            try
            {
                // Query the database for projects with the given GroupId
                var projects = await _context.Set<Project>()
                    .Where(p => p.GroupId == groupId)
                    .Select(p => new { p.ProjectId, p.Name })
                    .ToListAsync();

                // Check if any projects exist for the given GroupId
                if (!projects.Any())
                {
                    return NotFound(new { Message = "No projects found for the given GroupId." });
                }

                return Ok(projects);
            }
            catch (Exception ex)
            {
                // Handle errors gracefully
                return StatusCode(StatusCodes.Status500InternalServerError, new { Message = "An error occurred.", Details = ex.Message });
            }
        }


        // GET: api/Reports/GetLotNosByProjectId/{projectId}
        [HttpGet("GetLotNosByProjectId/{projectId}")]
        public async Task<IActionResult> GetLotNosByProjectId(int projectId)
        {
            try
            {
                // Query the database for unique LotNos of the given ProjectId
                var lotNos = await _context.Set<QuantitySheet>()
                    .Where(q => q.ProjectId == projectId && !string.IsNullOrEmpty(q.LotNo)) // Filter by ProjectId and non-null LotNo
                    .Select(q => q.LotNo)
                    .Distinct() // Ensure uniqueness
                    .ToListAsync();

                // Check if any LotNos exist for the given ProjectId
                if (lotNos == null || lotNos.Count == 0)
                {
                    return NotFound(new { Message = "No LotNos found for the given ProjectId." });
                }

                return Ok(lotNos);
            }
            catch (Exception ex)
            {
                // Handle errors gracefully
                return StatusCode(StatusCodes.Status500InternalServerError, new { Message = "An error occurred.", Details = ex.Message });
            }
        }



        [HttpGet("GetQuantitySheetsByProjectId/{projectId}/LotNo/{lotNo}")]
        public async Task<IActionResult> GetQuantitySheetsByProjectId(int projectId, string lotNo)
        {
            try
            {
                // Fetch QuantitySheet data by ProjectId and LotNo
                var quantitySheets = await _context.Set<QuantitySheet>()
                    .Where(q => q.ProjectId == projectId && q.LotNo == lotNo)
                    .ToListAsync();

                if (quantitySheets == null || quantitySheets.Count == 0)
                {
                    return NotFound(new { Message = "No data found for the given ProjectId and LotNo." });
                }

                // Fetch all necessary data
                var allProcesses = await _context.Set<Process>().ToListAsync();
                var transactions = await _context.Set<Transaction>()
                    .Where(t => t.ProjectId == projectId)
                    .ToListAsync();
                var allMachines = await _context.Set<Machine>().ToListAsync();
                var allZones = await _context.Set<Zone>().ToListAsync();
                var allTeams = await _context.Set<Team>().ToListAsync();
                var allUsers = await _context.Set<User>().ToListAsync();
                var dispatches = await _context.Set<Dispatch>()
                    .Where(d => d.ProjectId == projectId && d.LotNo == lotNo)
                    .ToListAsync(); // Fetch dispatch data

                // Map QuantitySheet data with required details
                var result = quantitySheets.Select(q =>
                {
                    // Get transactions related to this QuantitySheetId
                    var relatedTransactions = transactions
                        .Where(t => t.QuantitysheetId == q.QuantitySheetId)
                        .ToList();

                    string catchStatus;
                    if (!relatedTransactions.Any())
                    {
                        catchStatus = "Pending";
                    }
                    else
                    {
                        // Check if any transaction has ProcessId == 12
                        var process12Transaction = relatedTransactions.FirstOrDefault(t => t.ProcessId == 12);
                        if (process12Transaction != null && process12Transaction.Status == 2)
                        {
                            catchStatus = "Completed";
                        }
                        else if (relatedTransactions.Any(t => t.ProcessId != 12))
                        {
                            catchStatus = "Running";
                        }
                        else
                        {
                            catchStatus = "Pending";
                        }
                    }

                    var lastTransactionProcessId = relatedTransactions
                        .OrderByDescending(t => t.TransactionId) // Get the latest transaction based on TransactionId
                        .Select(t => t.ProcessId)
                        .FirstOrDefault();

                    var lastTransactionProcessName = allProcesses
                        .FirstOrDefault(p => p.Id == lastTransactionProcessId)?.Name;

                    // Get Dispatch Date if available, else return "Not Available"
                    var dispatchEntry = dispatches.FirstOrDefault(d => d.LotNo == q.LotNo);
                    var dispatchDate = dispatchEntry?.UpdatedAt.HasValue == true
                        ? dispatchEntry.UpdatedAt.Value.ToString("yyyy-MM-dd")
                        : "Not Available";

                    return new
                    {
                        q.CatchNo,
                        q.Paper,
                        q.ExamDate,
                        q.ExamTime,
                        q.Course,
                        q.Subject,
                        q.InnerEnvelope,
                        q.OuterEnvelope,
                        q.LotNo,
                        q.Quantity,
                        q.Pages,
                        q.Status,
                        ProcessNames = q.ProcessId != null
                            ? allProcesses
                                .Where(p => q.ProcessId.Contains(p.Id))
                                .Select(p => p.Name)
                                .ToList()
                            : null,
                        CatchStatus = catchStatus, // Updated logic
                        TwelvethProcess = relatedTransactions.Any(t => t.ProcessId == 12),
                        CurrentProcessName = lastTransactionProcessName,
                        DispatchDate = dispatchDate, // Added Dispatch Date
                                                     // Grouped Transaction Data
                        TransactionData = new
                        {
                            ZoneDescriptions = relatedTransactions
                                .Select(t => t.ZoneId)
                                .Distinct()
                                .Select(zoneId => allZones.FirstOrDefault(z => z.ZoneId == zoneId)?.ZoneDescription)
                                .Where(description => description != null)
                                .ToList(),
                            TeamDetails = relatedTransactions
                                .SelectMany(t => t.TeamId ?? new List<int>())
                                .Distinct()
                                .Select(teamId => new
                                {
                                    TeamName = allTeams.FirstOrDefault(t => t.TeamId == teamId)?.TeamName,
                                    UserNames = allTeams.FirstOrDefault(t => t.TeamId == teamId)?.UserIds
                                        .Select(userId => allUsers.FirstOrDefault(u => u.UserId == userId)?.UserName)
                                        .Where(userName => userName != null)
                                        .ToList()
                                })
                                .Where(team => team.TeamName != null)
                                .ToList(),
                            MachineNames = relatedTransactions
                                .Select(t => t.MachineId)
                                .Distinct()
                                .Select(machineId => allMachines.FirstOrDefault(m => m.MachineId == machineId)?.MachineName)
                                .Where(name => name != null)
                                .ToList()
                        }
                    };
                });

                return Ok(result);
            }
            catch (Exception ex)
            {
                // Handle errors gracefully
                return StatusCode(StatusCodes.Status500InternalServerError, new { Message = "An error occurred.", Details = ex.Message });
            }
        }



        [HttpGet("GetCatchNoByProject/{projectId}")]
        public async Task<IActionResult> GetCatchNoByProject(int projectId)
        {
            try
            {
                // Fetch all CatchNo where ProjectId matches and Status is 1
                var quantitySheets = await _context.QuantitySheets
                    .Where(q => q.ProjectId == projectId && q.Status == 1)
                    .Select(q => q.CatchNo)
                    .ToListAsync();

                if (quantitySheets == null || quantitySheets.Count == 0)
                {
                    return NotFound(new { Message = "No records found with Status = 1 for the given ProjectId." });
                }

                // Fetch event logs where category is 'Production' and projectId is present in OldValue or NewValue
                var eventLogs = await _context.EventLogs
                    .Where(e => e.Category == "Production" && (e.OldValue.Contains(projectId.ToString()) || e.NewValue.Contains(projectId.ToString())))
                    .Select(e => new { e.NewValue, e.LoggedAT })
                    .ToListAsync();

                if (eventLogs == null || eventLogs.Count == 0)
                {
                    return NotFound(new { Message = "No event logs found for the given ProjectId." });
                }

                return Ok(new { CatchNumbers = quantitySheets, Events = eventLogs });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { Message = "An error occurred.", Error = ex.Message });
            }
        }

        [HttpGet("search")]
        public async Task<IActionResult> SearchQuantitySheet(
    [FromQuery] string query,
    [FromQuery] int page = 1,
    [FromQuery] int pageSize = 5,
    [FromQuery] int? groupId = null,
    [FromQuery] int? projectId = null)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return BadRequest("Search query cannot be empty.");
            }

            var queryable = _context.QuantitySheets.AsQueryable();

            if (groupId.HasValue)
            {
                var projectIdsInGroup = _context.Projects
                    .Where(p => p.GroupId == groupId)
                    .Select(p => p.ProjectId);

                queryable = queryable.Where(q => projectIdsInGroup.Contains(q.ProjectId));
            }

            if (projectId.HasValue)
            {
                queryable = queryable.Where(q => q.ProjectId == projectId);
            }

            var totalRecords = await queryable
                .CountAsync(q => q.CatchNo.StartsWith(query) ||
                                q.Subject.StartsWith(query) ||
                                q.Course.StartsWith(query) ||
                                (q.Paper != null && q.Paper.StartsWith(query)));

            var results = await queryable
                .Where(q => q.CatchNo.StartsWith(query) ||
                            q.Subject.StartsWith(query) ||
                            q.Course.StartsWith(query) ||
                            (q.Paper != null && q.Paper.StartsWith(query)))
                .Select(q => new
                {
                    q.CatchNo,
                    //ProjectName = _context.Projects.Where(p => p.ProjectId == q.ProjectId).Select(p => p.Name).FirstOrDefault(),
                    //GroupName = _context.Groups.Where(g => g.Id == _context.Projects.Where(p => p.ProjectId == q.ProjectId).Select(p => p.GroupId).FirstOrDefault()).Select(g => g.Name).FirstOrDefault(),
                    MatchedColumn = q.CatchNo.StartsWith(query) ? "CatchNo" :
                                    q.Subject.StartsWith(query) ? "Subject" :
                                    q.Course.StartsWith(query) ? "Course" : "Paper",
                    MatchedValue = q.CatchNo.StartsWith(query) ? q.CatchNo :
                                   q.Subject.StartsWith(query) ? q.Subject :
                                   q.Course.StartsWith(query) ? q.Course : q.Paper,
                    q.ProjectId,
                    q.LotNo
                })
                .Skip((page - 1) * pageSize) // Skip records based on the page number
                .Take(pageSize) // Limit the number of results per page
                .ToListAsync();

            return Ok(new { TotalRecords = totalRecords, Results = results });
        }



        [HttpGet("GetQuantitySheetsByCatchNo/{projectId}/{catchNo}")]
        public async Task<IActionResult> GetQuantitySheetsByCatchNo(string catchNo, int projectId)
        {
            try
            {
                // Fetch QuantitySheet data by CatchNo
                var quantitySheets = await _context.Set<QuantitySheet>()
                    .Where(q => q.CatchNo == catchNo && q.ProjectId == projectId)
                    .ToListAsync();

                if (quantitySheets == null || quantitySheets.Count == 0)
                {
                    return NotFound(new { Message = "No data found for the given CatchNo." });
                }

                // Fetch all necessary data
                var allProcesses = await _context.Set<Process>().ToListAsync();
                var transactions = await _context.Set<Transaction>()
                    .Where(t => quantitySheets.Select(q => q.QuantitySheetId).Contains(t.QuantitysheetId))
                    .ToListAsync();
                var allMachines = await _context.Set<Machine>().ToListAsync();
                var allZones = await _context.Set<Zone>().ToListAsync();
                var allTeams = await _context.Set<Team>().ToListAsync();
                var allUsers = await _context.Set<User>().ToListAsync();
                var dispatches = await _context.Set<Dispatch>()
                    .Where(d => quantitySheets.Select(q => q.LotNo).Contains(d.LotNo))
                    .ToListAsync(); // Fetch dispatch data

                // Map QuantitySheet data with required details
                var result = quantitySheets.Select(q =>
                {
                    // Get transactions related to this QuantitySheetId
                    var relatedTransactions = transactions
                        .Where(t => t.QuantitysheetId == q.QuantitySheetId)
                        .ToList();

                    string catchStatus;
                    if (!relatedTransactions.Any())
                    {
                        catchStatus = "Pending";
                    }
                    else
                    {
                        // Check if any transaction has ProcessId == 12
                        var process12Transaction = relatedTransactions.FirstOrDefault(t => t.ProcessId == 12);
                        if (process12Transaction != null && process12Transaction.Status == 2)
                        {
                            catchStatus = "Completed";
                        }
                        else if (relatedTransactions.Any(t => t.ProcessId != 12))
                        {
                            catchStatus = "Running";
                        }
                        else
                        {
                            catchStatus = "Pending";
                        }
                    }

                    var lastTransactionProcessId = relatedTransactions
                        .OrderByDescending(t => t.TransactionId) // Get the latest transaction based on TransactionId
                        .Select(t => t.ProcessId)
                        .FirstOrDefault();

                    var lastTransactionProcessName = allProcesses
                        .FirstOrDefault(p => p.Id == lastTransactionProcessId)?.Name;

                    // Get Dispatch Date if available, else return "Not Available"
                    var dispatchEntry = dispatches.FirstOrDefault(d => d.LotNo == q.LotNo);
                    var dispatchDate = dispatchEntry?.UpdatedAt.HasValue == true
                        ? dispatchEntry.UpdatedAt.Value.ToString("yyyy-MM-dd")
                        : "Not Available";

                    return new
                    {
                        q.CatchNo,
                        q.Paper,
                        q.ExamDate,
                        q.ExamTime,
                        q.Course,
                        q.Subject,
                        q.InnerEnvelope,
                        q.OuterEnvelope,
                        q.LotNo,
                        q.Quantity,
                        q.Pages,
                        q.Status,
                        ProcessNames = q.ProcessId != null
                            ? allProcesses
                                .Where(p => q.ProcessId.Contains(p.Id))
                                .Select(p => p.Name)
                                .ToList()
                            : null,
                        CatchStatus = catchStatus, // Updated logic
                        TwelvethProcess = relatedTransactions.Any(t => t.ProcessId == 12),
                        CurrentProcessName = lastTransactionProcessName,
                        DispatchDate = dispatchDate, // Added Dispatch Date
                                                     // Grouped Transaction Data
                        TransactionData = new
                        {
                            ZoneDescriptions = relatedTransactions
                                .Select(t => t.ZoneId)
                                .Distinct()
                                .Select(zoneId => allZones.FirstOrDefault(z => z.ZoneId == zoneId)?.ZoneDescription)
                                .Where(description => description != null)
                                .ToList(),
                            TeamDetails = relatedTransactions
                                .SelectMany(t => t.TeamId ?? new List<int>())
                                .Distinct()
                                .Select(teamId => new
                                {
                                    TeamName = allTeams.FirstOrDefault(t => t.TeamId == teamId)?.TeamName,
                                    UserNames = allTeams.FirstOrDefault(t => t.TeamId == teamId)?.UserIds
                                        .Select(userId => allUsers.FirstOrDefault(u => u.UserId == userId)?.UserName)
                                        .Where(userName => userName != null)
                                        .ToList()
                                })
                                .Where(team => team.TeamName != null)
                                .ToList(),
                            MachineNames = relatedTransactions
                                .Select(t => t.MachineId)
                                .Distinct()
                                .Select(machineId => allMachines.FirstOrDefault(m => m.MachineId == machineId)?.MachineName)
                                .Where(name => name != null)
                                .ToList()
                        }
                    };
                });

                return Ok(result);
            }
            catch (Exception ex)
            {
                // Handle errors gracefully
                return StatusCode(StatusCodes.Status500InternalServerError, new { Message = "An error occurred.", Details = ex.Message });
            }
        }

        [HttpGet("Process-Production-Report")]
        public async Task<IActionResult> GetProcessWiseDataByDateRange(
  [FromQuery] string? date,
  [FromQuery] string? startDate,
  [FromQuery] string? endDate)
        {
            try
            {
                DateTime? parsedDate = null;
                DateTime? parsedStartDate = null;
                DateTime? parsedEndDate = null;

                if (!string.IsNullOrEmpty(date))
                {
                    if (!DateTime.TryParseExact(date, "dd-MM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed))
                        return BadRequest("Invalid date format. Use dd-MM-yyyy.");
                    parsedDate = parsed.Date;
                }

                if (!string.IsNullOrEmpty(startDate))
                {
                    if (!DateTime.TryParseExact(startDate, "dd-MM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsedStart))
                        return BadRequest("Invalid startDate format. Use dd-MM-yyyy.");
                    parsedStartDate = parsedStart.Date;
                }

                if (!string.IsNullOrEmpty(endDate))
                {
                    if (!DateTime.TryParseExact(endDate, "dd-MM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsedEnd))
                        return BadRequest("Invalid endDate format. Use dd-MM-yyyy.");
                    parsedEndDate = parsedEnd.Date;
                }

                // Step 1: Filter EventLogs
                var eventLogQuery = _context.EventLogs
                    .Where(el => el.Category == "Transaction"
                        && el.Event == "Status Updated"
                        && el.OldValue == "1"
                        && el.NewValue == "2"
                        && el.TransactionId != null);

                if (parsedStartDate.HasValue && parsedEndDate.HasValue)
                {
                    eventLogQuery = eventLogQuery.Where(el => el.LoggedAT.Date >= parsedStartDate.Value && el.LoggedAT.Date <= parsedEndDate.Value);
                }
                else if (parsedDate.HasValue)
                {
                    eventLogQuery = eventLogQuery.Where(el => el.LoggedAT.Date == parsedDate.Value);
                }

                var filteredLogs = await eventLogQuery.ToListAsync();
                var validTransactionIds = filteredLogs.Select(el => el.TransactionId.Value).Distinct().ToList();

                // Step 2: Get Transactions and their ProjectIds
                var transactions = await _context.Transaction
                    .Where(t => validTransactionIds.Contains(t.TransactionId))
                    .ToListAsync();

                var projectIds = transactions.Select(t => t.ProjectId).Distinct().ToList();

                // Step 3: Get Project with TypeId
                var projects = await _context.Projects
                    .Where(p => projectIds.Contains(p.ProjectId))
                    .ToDictionaryAsync(p => p.ProjectId, p => p.TypeId); // 1 = Booklet, 2 = Paper

                // Step 4: Segregate transactions by TypeId
                var bookletTransactionIds = transactions
                    .Where(t => projects.ContainsKey(t.ProjectId) && projects[t.ProjectId] == 1)
                    .Select(t => new { t.TransactionId, t.QuantitysheetId, t.ProcessId })
                    .ToList();

                var paperTransactionIds = transactions
                    .Where(t => projects.ContainsKey(t.ProjectId) && projects[t.ProjectId] == 2)
                    .Select(t => new { t.TransactionId, t.QuantitysheetId, t.ProcessId })
                    .ToList();

                var bookletSheetIds = bookletTransactionIds.Select(x => x.QuantitysheetId).Distinct().ToList();
                var paperSheetIds = paperTransactionIds.Select(x => x.QuantitysheetId).Distinct().ToList();

                var quantitySheets = await _context.QuantitySheets
                    .Where(qs => bookletSheetIds.Contains(qs.QuantitySheetId) || paperSheetIds.Contains(qs.QuantitySheetId))
                    .ToListAsync();

                var quantitySheetDict = quantitySheets.ToDictionary(qs => qs.QuantitySheetId, qs => qs.Quantity);

                // Step 5: Group and project
                var result = new List<object>();

                double totalCompletedBookletQuantity = 0;
                int totalCompletedBookletCatch = 0;

                double totalCompletedPaperQuantity = 0;
                int totalCompletedPaperCatch = 0;

                var allProcessIds = transactions.Select(t => t.ProcessId).Distinct();

                foreach (var processId in allProcessIds)
                {
                    var bookletCatches = bookletTransactionIds
                        .Where(t => t.ProcessId == processId)
                        .GroupBy(t => t.QuantitysheetId)
                        .Select(g => g.Key)
                        .Distinct()
                        .ToList();

                    var paperCatches = paperTransactionIds
                        .Where(t => t.ProcessId == processId)
                        .GroupBy(t => t.QuantitysheetId)
                        .Select(g => g.Key)
                        .Distinct()
                        .ToList();

                    var bookletQuantity = bookletCatches
                        .Where(qid => quantitySheetDict.ContainsKey(qid))
                        .Sum(qid => quantitySheetDict[qid]);

                    var paperQuantity = paperCatches
                        .Where(qid => quantitySheetDict.ContainsKey(qid))
                        .Sum(qid => quantitySheetDict[qid]);

                    // Accumulate overall totals
                    totalCompletedBookletCatch += bookletCatches.Count;
                    totalCompletedBookletQuantity += bookletQuantity;

                    totalCompletedPaperCatch += paperCatches.Count;
                    totalCompletedPaperQuantity += paperQuantity;

                    result.Add(new
                    {
                        ProcessId = processId,
                        CompletedTotalCatchesInBooklet = bookletCatches.Count,
                        CompletedTotalQuantityInBooklet = bookletQuantity,
                        CompletedTotalCatchesInPaper = paperCatches.Count,
                        CompletedTotalQuantityInPaper = paperQuantity
                    });
                }

                // Add grand total object at the end
                result.Add(new
                {
                    ProcessId = "Total",
                    CompletedTotalCatchesInBooklet = totalCompletedBookletCatch,
                    CompletedTotalQuantityInBooklet = totalCompletedBookletQuantity,
                    CompletedTotalCatchesInPaper = totalCompletedPaperCatch,
                    CompletedTotalQuantityInPaper = totalCompletedPaperQuantity
                });

                return Ok(result.OrderBy(x => ((dynamic)x).ProcessId.ToString()).ToList());
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "An error occurred", error = ex.Message });
            }
        }


        [HttpGet("Process-Production-Report-Project-Wise")]
        public async Task<IActionResult> GetProcessWiseProjectReportByDateRange(
[FromQuery] string? date,
[FromQuery] string? startDate,
[FromQuery] string? endDate,
[FromQuery] int? processId)
        {
            try
            {
                if (!processId.HasValue || processId.Value <= 0)
                    return BadRequest("processId is required and must be greater than 0.");

                DateTime? parsedDate = null;
                DateTime? parsedStartDate = null;
                DateTime? parsedEndDate = null;

                if (!string.IsNullOrEmpty(date))
                {
                    if (!DateTime.TryParseExact(date, "dd-MM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed))
                        return BadRequest("Invalid date format. Use dd-MM-yyyy.");
                    parsedDate = parsed.Date;
                }

                if (!string.IsNullOrEmpty(startDate))
                {
                    if (!DateTime.TryParseExact(startDate, "dd-MM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsedStart))
                        return BadRequest("Invalid startDate format. Use dd-MM-yyyy.");
                    parsedStartDate = parsedStart.Date;
                }

                if (!string.IsNullOrEmpty(endDate))
                {
                    if (!DateTime.TryParseExact(endDate, "dd-MM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsedEnd))
                        return BadRequest("Invalid endDate format. Use dd-MM-yyyy.");
                    parsedEndDate = parsedEnd.Date;
                }

                // Step 1: Filter EventLogs
                var eventLogQuery = _context.EventLogs
                    .Where(el => el.Category == "Transaction"
                        && el.Event == "Status Updated"
                        && el.OldValue == "1"
                        && el.NewValue == "2"
                        && el.TransactionId != null);

                if (parsedStartDate.HasValue && parsedEndDate.HasValue)
                {
                    eventLogQuery = eventLogQuery.Where(el => el.LoggedAT.Date >= parsedStartDate.Value && el.LoggedAT.Date <= parsedEndDate.Value);
                }
                else if (parsedDate.HasValue)
                {
                    eventLogQuery = eventLogQuery.Where(el => el.LoggedAT.Date == parsedDate.Value);
                }

                var filteredLogs = await eventLogQuery.ToListAsync();
                var validTransactionIds = filteredLogs.Select(el => el.TransactionId.Value).Distinct().ToList();

                // Step 2: Get Transactions and their ProjectIds
                var transactions = await _context.Transaction
                    .Where(t => validTransactionIds.Contains(t.TransactionId) && t.ProcessId == processId)
                    .ToListAsync();

                if (!transactions.Any())
                    return Ok(new List<object>());

                var projectIds = transactions.Select(t => t.ProjectId).Distinct().ToList();

                // Step 3: Get Project with TypeId
                var projects = await _context.Projects
                    .Where(p => projectIds.Contains(p.ProjectId))
                    .ToDictionaryAsync(p => p.ProjectId, p => p.TypeId); // 1 = Booklet, 2 = Paper

                // Step 4: Segregate transactions by TypeId
                var bookletTransactions = transactions
                    .Where(t => projects.ContainsKey(t.ProjectId) && projects[t.ProjectId] == 1)
                    .Select(t => new { t.TransactionId, t.QuantitysheetId, t.ProjectId })
                    .ToList();

                var paperTransactions = transactions
                    .Where(t => projects.ContainsKey(t.ProjectId) && projects[t.ProjectId] == 2)
                    .Select(t => new { t.TransactionId, t.QuantitysheetId, t.ProjectId })
                    .ToList();

                var allSheetIds = bookletTransactions.Select(t => t.QuantitysheetId)
                    .Concat(paperTransactions.Select(t => t.QuantitysheetId))
                    .Distinct()
                    .ToList();

                var quantitySheets = await _context.QuantitySheets
                    .Where(qs => allSheetIds.Contains(qs.QuantitySheetId))
                    .ToDictionaryAsync(qs => qs.QuantitySheetId, qs => qs.Quantity);

                // Step 5: Group by Project
                var result = new List<object>();

                var allProjectIds = transactions.Select(t => t.ProjectId).Distinct();

                foreach (var projId in allProjectIds)
                {
                    var bookletCatches = bookletTransactions
                        .Where(t => t.ProjectId == projId)
                        .GroupBy(t => t.QuantitysheetId)
                        .Select(g => g.Key)
                        .Distinct()
                        .ToList();

                    var paperCatches = paperTransactions
                        .Where(t => t.ProjectId == projId)
                        .GroupBy(t => t.QuantitysheetId)
                        .Select(g => g.Key)
                        .Distinct()
                        .ToList();

                    var bookletQuantity = bookletCatches
                        .Where(qid => quantitySheets.ContainsKey(qid))
                        .Sum(qid => quantitySheets[qid]);

                    var paperQuantity = paperCatches
                        .Where(qid => quantitySheets.ContainsKey(qid))
                        .Sum(qid => quantitySheets[qid]);

                    result.Add(new
                    {
                        ProjectId = projId,
                        CompletedTotalCatchesInBooklet = bookletCatches.Count,
                        CompletedTotalQuantityInBooklet = bookletQuantity,
                        CompletedTotalCatchesInPaper = paperCatches.Count,
                        CompletedTotalQuantityInPaper = paperQuantity
                    });
                }

                return Ok(result.OrderBy(x => ((dynamic)x).ProjectId).ToList());
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "An error occurred", error = ex.Message });
            }
        }




        [HttpGet("Process-Production-Report-Group-Wise")]
        public async Task<IActionResult> GetGroupProcessWiseProjectReportByDateRange(
     [FromQuery] string? date,
     [FromQuery] string? startDate,
     [FromQuery] string? endDate,
     [FromQuery] int? processId,
     [FromQuery] int? groupId,
     [FromQuery] int? projectId)
        {
            try
            {
                DateTime? parsedDate = null;
                DateTime? parsedStartDate = null;
                DateTime? parsedEndDate = null;

                if (!string.IsNullOrEmpty(date) && DateTime.TryParseExact(date, "dd-MM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed))
                    parsedDate = parsed.Date;
                else if (!string.IsNullOrEmpty(date))
                    return BadRequest("Invalid date format. Use dd-MM-yyyy.");

                if (!string.IsNullOrEmpty(startDate) && DateTime.TryParseExact(startDate, "dd-MM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsedStart))
                    parsedStartDate = parsedStart.Date;
                else if (!string.IsNullOrEmpty(startDate))
                    return BadRequest("Invalid startDate format. Use dd-MM-yyyy.");

                if (!string.IsNullOrEmpty(endDate) && DateTime.TryParseExact(endDate, "dd-MM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsedEnd))
                    parsedEndDate = parsedEnd.Date;
                else if (!string.IsNullOrEmpty(endDate))
                    return BadRequest("Invalid endDate format. Use dd-MM-yyyy.");

                var eventLogQuery = _context.EventLogs
                    .Where(el => el.Category == "Transaction"
                        && el.Event == "Status Updated"
                        && el.OldValue == "1"
                        && el.NewValue == "2"
                        && el.TransactionId != null);

                if (parsedStartDate.HasValue && parsedEndDate.HasValue)
                    eventLogQuery = eventLogQuery.Where(el => el.LoggedAT.Date >= parsedStartDate.Value && el.LoggedAT.Date <= parsedEndDate.Value);
                else if (parsedDate.HasValue)
                    eventLogQuery = eventLogQuery.Where(el => el.LoggedAT.Date == parsedDate.Value);

                var filteredLogs = await eventLogQuery.ToListAsync();
                var validTransactionIds = filteredLogs.Select(el => el.TransactionId.Value).Distinct().ToList();

                var transactionQuery = _context.Transaction.Where(t => validTransactionIds.Contains(t.TransactionId));

                if (processId.HasValue && processId.Value > 0)
                    transactionQuery = transactionQuery.Where(t => t.ProcessId == processId.Value);

                if (projectId.HasValue && projectId.Value > 0)
                    transactionQuery = transactionQuery.Where(t => t.ProjectId == projectId.Value);

                var allMatchingTransactions = await transactionQuery.ToListAsync();

                if (!allMatchingTransactions.Any())
                    return Ok(new List<object>());

                var involvedProjectIds = allMatchingTransactions.Select(t => t.ProjectId).Distinct().ToList();

                var projectQuery = _context.Projects.Where(p => involvedProjectIds.Contains(p.ProjectId));

                if (groupId.HasValue && groupId.Value > 0)
                    projectQuery = projectQuery.Where(p => p.GroupId == groupId.Value);

                var projects = await projectQuery
                    .ToDictionaryAsync(p => p.ProjectId, p => new { p.TypeId, p.GroupId });

                var validTransactions = allMatchingTransactions
                    .Where(t => projects.ContainsKey(t.ProjectId))
                    .ToList();

                if (!validTransactions.Any())
                    return Ok(new List<object>());

                var bookletTransactions = validTransactions
                    .Where(t => projects[t.ProjectId].TypeId == 1)
                    .Select(t => new { t.TransactionId, t.QuantitysheetId, t.ProjectId, t.LotNo })
                    .ToList();

                var paperTransactions = validTransactions
                    .Where(t => projects[t.ProjectId].TypeId == 2)
                    .Select(t => new { t.TransactionId, t.QuantitysheetId, t.ProjectId, t.LotNo })
                    .ToList();

                var allSheetIds = bookletTransactions.Select(t => t.QuantitysheetId)
                    .Concat(paperTransactions.Select(t => t.QuantitysheetId))
                    .Distinct()
                    .ToList();

                var quantitySheets = await _context.QuantitySheets
                    .Where(qs => allSheetIds.Contains(qs.QuantitySheetId))
                    .ToDictionaryAsync(qs => qs.QuantitySheetId, qs => qs.Quantity);

                var result = new List<object>();

                if (groupId.HasValue || projectId.HasValue)
                {
                    foreach (var projId in validTransactions.Select(t => t.ProjectId).Distinct())
                    {
                        var bookletCatches = bookletTransactions
                            .Where(t => t.ProjectId == projId)
                            .Select(t => t.QuantitysheetId)
                            .Distinct()
                            .ToList();

                        var paperCatches = paperTransactions
                            .Where(t => t.ProjectId == projId)
                            .Select(t => t.QuantitysheetId)
                            .Distinct()
                            .ToList();

                        var bookletQuantity = bookletCatches
                            .Where(qid => quantitySheets.ContainsKey(qid))
                            .Sum(qid => quantitySheets[qid]);

                        var paperQuantity = paperCatches
                            .Where(qid => quantitySheets.ContainsKey(qid))
                            .Sum(qid => quantitySheets[qid]);

                        var lotNos = bookletTransactions
                            .Where(t => t.ProjectId == projId)
                            .Select(t => t.LotNo)
                            .Concat(
                                paperTransactions
                                    .Where(t => t.ProjectId == projId)
                                    .Select(t => t.LotNo)
                            )
                            .Distinct()
                            .ToList();

                        result.Add(new
                        {
                            ProjectId = projId,
                            GroupId = projects[projId].GroupId,
                            CompletedTotalCatchesInBooklet = bookletCatches.Count,
                            CompletedTotalQuantityInBooklet = bookletQuantity,
                            CompletedTotalCatchesInPaper = paperCatches.Count,
                            CompletedTotalQuantityInPaper = paperQuantity,
                            BookletCatchList = bookletCatches,
                            PaperCatchList = paperCatches,
                            LotNos = lotNos
                        });
                    }
                }
                else
                {
                    foreach (var group in projects.GroupBy(p => p.Value.GroupId))
                    {
                        var groupIdValue = group.Key;
                        var projectsInGroup = group.ToList();

                        var bookletCatches = bookletTransactions
                            .Where(t => projectsInGroup.Any(p => p.Key == t.ProjectId))
                            .Select(t => t.QuantitysheetId)
                            .Distinct()
                            .ToList();

                        var paperCatches = paperTransactions
                            .Where(t => projectsInGroup.Any(p => p.Key == t.ProjectId))
                            .Select(t => t.QuantitysheetId)
                            .Distinct()
                            .ToList();

                        var bookletQuantity = bookletCatches
                            .Where(qid => quantitySheets.ContainsKey(qid))
                            .Sum(qid => quantitySheets[qid]);

                        var paperQuantity = paperCatches
                            .Where(qid => quantitySheets.ContainsKey(qid))
                            .Sum(qid => quantitySheets[qid]);

                        result.Add(new
                        {
                            GroupId = groupIdValue,
                            CompletedTotalCatchesInBooklet = bookletCatches.Count,
                            CompletedTotalQuantityInBooklet = bookletQuantity,
                            CompletedTotalCatchesInPaper = paperCatches.Count,
                            CompletedTotalQuantityInPaper = paperQuantity
                        });
                    }
                }

                return Ok(result.OrderBy(x => ((dynamic)x).GroupId).ToList());
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "An error occurred", error = ex.Message });
            }
        }



        [HttpGet("process-wise/{projectId}/{catchNo}")]
        public async Task<IActionResult> GetProcessWiseData(int projectId, string catchNo)
        {
            var quantitySheet = await _context.QuantitySheets
                .Where(q => q.ProjectId == projectId && q.CatchNo == catchNo)
                .Select(q => new { q.QuantitySheetId, q.ProcessId, q.ProjectId })
                .FirstOrDefaultAsync();

            if (quantitySheet == null)
                return NotFound("No data found for the given ProjectId and CatchNo.");

            var projectProcesses = await _context.ProjectProcesses
                .Where(pp => pp.ProjectId == quantitySheet.ProjectId)
                .OrderBy(pp => pp.Sequence)
                .ToListAsync();

            var transactions = await _context.Transaction
                .Where(t => t.QuantitysheetId == quantitySheet.QuantitySheetId)
                .ToListAsync();

            var transactionIds = transactions.Select(t => t.TransactionId).ToList();

            var eventLogs = await _context.EventLogs
                .Where(e => transactionIds.Contains(e.TransactionId.Value) && e.Event == "Status updated")
                .Select(e => new { e.TransactionId, e.LoggedAT, e.EventTriggeredBy })
                .ToListAsync();

            var supervisorLogs = await _context.EventLogs
                .Where(e => transactionIds.Contains(e.TransactionId.Value))
                .GroupBy(e => e.TransactionId)
                .Select(g => new
                {
                    TransactionId = g.Key,
                    EventTriggeredBy = g.Select(e => e.EventTriggeredBy).FirstOrDefault()
                })
                .ToListAsync();

            var users = await _context.Users
                .Select(u => new { u.UserId, FullName = u.FirstName + " " + u.LastName })
                .ToListAsync();

            var zones = await _context.Zone
                .Select(z => new { z.ZoneId, z.ZoneNo })
                .ToListAsync();

            var machines = await _context.Machine
                .Select(m => new { m.MachineId, m.MachineName })
                .ToListAsync();

            // Convert to dictionaries for quick lookup (O(1))
            var zoneMap = zones.ToDictionary(z => z.ZoneId, z => z.ZoneNo);
            var machineMap = machines.ToDictionary(m => m.MachineId, m => m.MachineName);
            var userMap = users.ToDictionary(u => u.UserId, u => u.FullName);
            var supervisorMap = supervisorLogs.ToDictionary(s => s.TransactionId, s => s.EventTriggeredBy);

            var filteredProjectProcesses = projectProcesses
                .Where(pp => transactions.Any(t => t.ProcessId == pp.ProcessId))
                .OrderBy(pp => pp.Sequence)
                .ToList();

            var processWiseData = filteredProjectProcesses
                .Select(pp => new
                {
                    ProcessId = pp.ProcessId,
                    Transactions = transactions
                        .Where(t => t.ProcessId == pp.ProcessId)
                        .Select(t => new
                        {
                            TransactionId = t.TransactionId,
                            ZoneName = zoneMap.TryGetValue(t.ZoneId, out var zoneName) ? zoneName : null,
                            TeamMembers = users
                                .Where(u => t.TeamId.Contains(u.UserId))
                                .Select(u => new { u.FullName })
                                .ToList(),
                            Supervisor = supervisorMap.TryGetValue(t.TransactionId, out var supervisorId)
                                && userMap.TryGetValue(supervisorId, out var supervisorName)
                                ? supervisorName
                                : null,
                            MachineName = machineMap.TryGetValue(t.MachineId, out var machineName) ? machineName : null,
                            StartTime = eventLogs
                                .Where(e => e.TransactionId == t.TransactionId)
                                .OrderBy(e => e.LoggedAT)
                                .Select(e => (DateTime?)e.LoggedAT)
                                .FirstOrDefault(),
                            EndTime = eventLogs
                                .Where(e => e.TransactionId == t.TransactionId)
                                .OrderByDescending(e => e.LoggedAT)
                                .Select(e => (DateTime?)e.LoggedAT)
                                .FirstOrDefault()
                        })
                        .ToList()
                })
                .ToList();

            return Ok(processWiseData);
        }
        [HttpGet("DailyProductionReport")]
        public async Task<IActionResult> GetDailyProductionReport(string? date = null, string? startDate = null, string? endDate = null)
        {
            try
            {
                DateTime? parsedDate = null;
                DateTime? parsedStartDate = null;
                DateTime? parsedEndDate = null;

                if (!string.IsNullOrEmpty(date))
                {
                    if (!DateTime.TryParseExact(date, "dd-MM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed))
                        return BadRequest("Invalid date format. Use dd-MM-yyyy.");
                    parsedDate = parsed.Date;
                }

                if (!string.IsNullOrEmpty(startDate))
                {
                    if (!DateTime.TryParseExact(startDate, "dd-MM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsedStart))
                        return BadRequest("Invalid startDate format. Use dd-MM-yyyy.");
                    parsedStartDate = parsedStart.Date;
                }

                if (!string.IsNullOrEmpty(endDate))
                {
                    if (!DateTime.TryParseExact(endDate, "dd-MM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsedEnd))
                        return BadRequest("Invalid endDate format. Use dd-MM-yyyy.");
                    parsedEndDate = parsedEnd.Date;
                }

                var logQuery = _context.EventLogs.AsQueryable();

                // Filter by date range
                if (parsedStartDate.HasValue && parsedEndDate.HasValue)
                {
                    logQuery = logQuery.Where(el => el.LoggedAT.Date >= parsedStartDate.Value && el.LoggedAT.Date <= parsedEndDate.Value);
                }
                else if (parsedDate.HasValue)
                {
                    logQuery = logQuery.Where(el => el.LoggedAT.Date == parsedDate.Value);
                }

                // Filter by event type and new value = 2
                logQuery = logQuery.Where(el => el.Event == "Status updated" && el.NewValue == "2");

                var transactionIds = await logQuery
                    .Where(el => el.TransactionId.HasValue)
                    .Select(el => el.TransactionId.Value)
                    .Distinct()
                    .ToListAsync();

                var transactions = await _context.Transaction
                    .Where(t => transactionIds.Contains(t.TransactionId))
                    .ToListAsync();

                var quantitySheetIds = transactions.Select(t => t.QuantitysheetId).Distinct().ToList();
                var quantitySheets = await _context.QuantitySheets
                    .Where(q => quantitySheetIds.Contains(q.QuantitySheetId))
                    .ToListAsync();

                var projectIds = transactions.Select(t => t.ProjectId).Distinct().ToList();
                var projects = await _context.Projects
                    .Where(p => projectIds.Contains(p.ProjectId))
                    .ToListAsync();

                var groupIds = projects.Select(p => p.GroupId).Distinct().ToList();
                var groups = await _context.Groups
                    .Where(g => groupIds.Contains(g.Id))
                    .ToDictionaryAsync(g => g.Id, g => g.Name);

                var report = transactions
                    .Join(projects, t => t.ProjectId, p => p.ProjectId, (t, p) => new { t, p })
                    .Join(quantitySheets, tp => tp.t.QuantitysheetId, qs => qs.QuantitySheetId, (tp, qs) => new { tp.t, tp.p, qs })
                    .GroupBy(x => new { x.t.ProjectId, x.p.TypeId, x.p.GroupId, x.t.LotNo })
                    .Select(g =>
                    {
                        var examDates = g
                            .Select(x =>
                            {
                                DateTime.TryParse(x.qs.ExamDate, out var dt);
                                return dt;
                            })
                            .Where(d => d != default)
                            .ToList();

                        var minExamDate = examDates.Any() ? examDates.Min().ToString("dd-MM-yyyy") : null;
                        var maxExamDate = examDates.Any() ? examDates.Max().ToString("dd-MM-yyyy") : null;

                        return new
                        {
                            GroupName = groups.ContainsKey(g.Key.GroupId) ? groups[g.Key.GroupId] : "Unknown",
                            ProjectId = g.Key.ProjectId,
                            TypeId = g.Key.TypeId,
                            LotNo = g.Key.LotNo,
                            To = minExamDate,
                            From = maxExamDate,
                            CountOfCatches = g.Count(),
                            TotalQuantity = g.Sum(x => x.qs.Quantity)
                        };
                    })
.ToList();


                return Ok(report);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "An error occurred", error = ex.Message });
            }
        }

        [HttpGet("DailyProductionSummaryReport")]
        public async Task<IActionResult> GetDailyProductionSummaryReport(string? date = null, string? startDate = null, string? endDate = null)
        {
            try
            {
                DateTime? parsedDate = null;
                DateTime? parsedStartDate = null;
                DateTime? parsedEndDate = null;

                if (!string.IsNullOrEmpty(date))
                {
                    if (!DateTime.TryParseExact(date, "dd-MM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed))
                        return BadRequest("Invalid date format. Use dd-MM-yyyy.");
                    parsedDate = parsed.Date;
                }

                if (!string.IsNullOrEmpty(startDate))
                {
                    if (!DateTime.TryParseExact(startDate, "dd-MM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsedStart))
                        return BadRequest("Invalid startDate format. Use dd-MM-yyyy.");
                    parsedStartDate = parsedStart.Date;
                }

                if (!string.IsNullOrEmpty(endDate))
                {
                    if (!DateTime.TryParseExact(endDate, "dd-MM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsedEnd))
                        return BadRequest("Invalid endDate format. Use dd-MM-yyyy.");
                    parsedEndDate = parsedEnd.Date;
                }

                var logQuery = _context.EventLogs.AsQueryable();

                if (parsedStartDate.HasValue && parsedEndDate.HasValue)
                {
                    logQuery = logQuery.Where(el => el.LoggedAT.Date >= parsedStartDate.Value && el.LoggedAT.Date <= parsedEndDate.Value);
                }
                else if (parsedDate.HasValue)
                {
                    logQuery = logQuery.Where(el => el.LoggedAT.Date == parsedDate.Value);
                }

                logQuery = logQuery.Where(el => el.Event == "Status updated" && el.NewValue == "2");

                var transactionIds = await logQuery
                    .Where(el => el.TransactionId.HasValue)
                    .Select(el => el.TransactionId.Value)
                    .Distinct()
                    .ToListAsync();

                var transactions = await _context.Transaction
                    .Where(t => transactionIds.Contains(t.TransactionId))
                    .ToListAsync();

                var quantitySheetIds = transactions.Select(t => t.QuantitysheetId).Distinct().ToList();
                var quantitySheets = await _context.QuantitySheets
                    .Where(q => quantitySheetIds.Contains(q.QuantitySheetId))
                    .ToListAsync();

                var projectIds = transactions.Select(t => t.ProjectId).Distinct().ToList();
                var projects = await _context.Projects
                    .Where(p => projectIds.Contains(p.ProjectId))
                    .ToListAsync();

                // Join and group
                var joinedData = transactions
                    .Join(projects, t => t.ProjectId, p => p.ProjectId, (t, p) => new { t, p })
                    .Join(quantitySheets, tp => tp.t.QuantitysheetId, qs => qs.QuantitySheetId, (tp, qs) => new { tp.t, tp.p, qs });

                // Grouped to calculate final summary
                var grouped = joinedData
                    .GroupBy(x => new { x.t.ProjectId, x.p.TypeId, x.p.GroupId, x.t.LotNo })
                    .ToList();

                var totalGroups = grouped.Select(g => g.Key.GroupId).Distinct().Count();
                var totalLots = grouped.Count(); // Total number of grouped lot entries (not distinct)
                var totalProjects = grouped.Select(g => g.Key.ProjectId).Distinct().Count();
                var totalCatches = grouped.Sum(g => g.Count());
                var totalQuantity = grouped.Sum(g => g.Sum(x => x.qs.Quantity));

                return Ok(new
                {
                    TotalGroups = totalGroups,
                    TotalLots = totalLots,
                    TotalCountOfCatches = totalCatches,
                    TotalProjects = totalProjects,
                    TotalQuantity = totalQuantity
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "An error occurred", error = ex.Message });
            }
        }

        [HttpGet("quickCompletion")]
        public async Task<IActionResult> GetQuickCompletion(
[FromQuery] string? date,
[FromQuery] string? startDate,
[FromQuery] string? endDate,
[FromQuery] int page = 1,
[FromQuery] int pageSize = 10)
        {
            DateTime startDateTime, endDateTime;

            if (!string.IsNullOrEmpty(date))
            {
                if (!DateTime.TryParseExact(date, "dd-MM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out startDateTime))
                {
                    return BadRequest("Invalid 'date' format. Use dd-MM-yyyy.");
                }
                endDateTime = startDateTime.AddDays(1);
            }
            else if (!string.IsNullOrEmpty(startDate) && !string.IsNullOrEmpty(endDate))
            {
                if (!DateTime.TryParseExact(startDate, "dd-MM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out startDateTime))
                    return BadRequest("Invalid 'startDate' format. Use dd-MM-yyyy.");

                if (!DateTime.TryParseExact(endDate, "dd-MM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out endDateTime))
                    return BadRequest("Invalid 'endDate' format. Use dd-MM-yyyy.");

                endDateTime = endDateTime.AddDays(1); // Make endDate inclusive
            }
            else
            {
                return BadRequest("Please provide either 'date' or both 'startDate' and 'endDate'.");
            }

            var logs = await _context.EventLogs
                .Where(e => e.Event == "Status updated"
                            && e.LoggedAT >= startDateTime
                            && e.LoggedAT < endDateTime)
                .ToListAsync();

            var transactionIds = logs.Select(e => e.TransactionId).Distinct().ToList();

            var transactions = await _context.Transaction
                .Where(t => transactionIds.Contains(t.TransactionId))
                .ToListAsync();

            var quantitySheetIds = transactions.Select(t => t.QuantitysheetId).Distinct().ToList();

            var quantitySheets = await _context.QuantitySheets
                .Where(qs => quantitySheetIds.Contains(qs.QuantitySheetId))
                .ToListAsync();

            var projectIds = transactions.Select(t => t.ProjectId).Distinct().ToList();
            var projects = await _context.Projects
                .Where(p => projectIds.Contains(p.ProjectId))
                .ToListAsync();

            var enrichedLogs = (from log in logs
                                join txn in transactions on log.TransactionId equals txn.TransactionId into txnJoin
                                from txn in txnJoin.DefaultIfEmpty()
                                join qs in quantitySheets on txn?.QuantitysheetId equals qs.QuantitySheetId into qsJoin
                                from qs in qsJoin.DefaultIfEmpty()
                                join proj in projects on txn?.ProjectId equals proj.ProjectId into projJoin
                                from proj in projJoin.DefaultIfEmpty()
                                select new
                                {
                                    Log = log,
                                    TransactionId = txn?.TransactionId,
                                    QuantitySheetId = txn?.QuantitysheetId,
                                    ProjectId = txn?.ProjectId,
                                    GroupId = proj?.GroupId,
                                    CatchNo = qs?.CatchNo,
                                    Quantity = qs?.Quantity
                                }).ToList();

            var matchedLogs = (from a in enrichedLogs
                               from b in enrichedLogs
                               where a.Log.TransactionId == b.Log.TransactionId
                                     && a.Log.EventID != b.Log.EventID
                                     && Math.Abs((a.Log.LoggedAT - b.Log.LoggedAT).TotalMinutes) < 5
                               orderby a.Log.TransactionId, a.Log.LoggedAT
                               select new
                               {
                                   EventID_A = a.Log.EventID,
                                   EventID_B = b.Log.EventID,
                                   Event_A = a.Log.Event,
                                   Event_B = b.Log.Event,
                                   a.TransactionId,
                                   a.ProjectId,
                                   a.GroupId,
                                   a.QuantitySheetId,
                                   a.CatchNo,
                                   a.Quantity,
                                   LoggedAT_A = a.Log.LoggedAT,
                                   LoggedAT_B = b.Log.LoggedAT,
                                   TriggeredBy_A = a.Log.EventTriggeredBy,
                                   TriggeredBy_B = b.Log.EventTriggeredBy,
                                   TimeDifferenceMinutes = (int)Math.Abs((a.Log.LoggedAT - b.Log.LoggedAT).TotalMinutes)
                               }).ToList();

            var totalItems = matchedLogs.Count;
            var paginatedResult = matchedLogs
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            return Ok(new
            {
                StartDate = startDateTime.ToString("dd-MM-yyyy"),
                EndDate = endDateTime.AddDays(-1).ToString("dd-MM-yyyy"),
                Page = page,
                PageSize = pageSize,
                TotalItems = totalItems,
                TotalPages = (int)Math.Ceiling(totalItems / (double)pageSize),
                Items = paginatedResult
            });
        }



        [HttpGet("project-lotno-with-status")]
        public async Task<IActionResult> GetProjectIdAndLotNoWithStatus(
      [FromQuery] int? groupId = null,
      [FromQuery] int? projectId = null)
        {
            try
            {
                // Step 1: Load project map with GroupId, Name, and Status
                var projectMap = await _context.Projects
                    .Select(p => new { p.ProjectId, p.GroupId, p.Name, p.Status })
                    .ToDictionaryAsync(p => p.ProjectId, p => new { p.GroupId, p.Name, p.Status });

                var thresholdDateString = "2025-06-25T00:00:00.000Z";

                // Step 2: Get valid QuantitySheets
                var quantitySheets = await _context.QuantitySheets
                    .Where(q => q.Status == 1 && string.Compare(q.ExamDate, thresholdDateString) >= 0)
                    .Select(q => new { q.ProjectId, q.LotNo, q.ExamDate, q.QuantitySheetId, q.Quantity })
                    .ToListAsync();

                // Step 3: Get dispatched project-lot combinations
                var dispatchedKeys = await _context.Dispatch
                    .Select(d => new { d.ProjectId, d.LotNo })
                    .ToListAsync();

                var dispatchedSet = new HashSet<string>(
                    dispatchedKeys.Select(d => $"{d.ProjectId}|{d.LotNo}")
                );

                // Step 4: Group quantitySheets and exclude dispatched
                var quantitySheetGroups = quantitySheets
                    .GroupBy(q => new { q.ProjectId, q.LotNo })
                    .Where(g => !dispatchedSet.Contains($"{g.Key.ProjectId}|{g.Key.LotNo}"))
                    .ToList();

                // ✅ Default case: return distinct GroupIds with Names (only one entry per GroupId)
                if (!groupId.HasValue && !projectId.HasValue)
                {
                    var result = quantitySheetGroups
                        .Select(g => g.Key.ProjectId)
                        .Distinct()
                        .Where(pid => projectMap.ContainsKey(pid))
                        .Select(pid => new
                        {
                            GroupId = projectMap[pid].GroupId,
                            Name = projectMap[pid].Name
                        })
                        .GroupBy(x => x.GroupId)
                        .Select(g => g.First()) // Ensure one entry per GroupId
                        .ToList();

                    return Ok(result);
                }

                // ✅ Filter based on input parameters
                var filteredData = quantitySheetGroups
                    .Where(g =>
                        (!groupId.HasValue || (projectMap.TryGetValue(g.Key.ProjectId, out var pd) && pd.GroupId == groupId.Value)) &&
                            (!projectId.HasValue || g.Key.ProjectId == projectId.Value))
                           .ToList();

                // ✅ If groupId only: return ProjectId + Name
                if (groupId.HasValue && !projectId.HasValue)
                {
                    var result = filteredData
                        .Select(g => g.Key.ProjectId)
                        .Distinct()
                        .Where(pid => projectMap.ContainsKey(pid))
                        .Select(pid => new
                        {
                            ProjectId = pid,
                            Name = projectMap[pid].Name
                        })
                        .ToList();

                    return Ok(result);
                }

                // ✅ If projectId only: return LotNo
                if (projectId.HasValue)
                {
                    var result = filteredData
                        .Where(g => g.Key.ProjectId == projectId.Value)
                        .Select(g => new
                        {
                            LotNo = g.Key.LotNo
                        })
                        .Distinct()
                        .ToList();

                    return Ok(result);
                }

                return Ok(new { message = "Invalid parameter combination" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "An error occurred", error = ex.Message });
            }
        }




        [HttpGet("pending-process-report-from-quantitysheet")]
        public async Task<IActionResult> GetPendingProcessReportFromQuantitySheet(
   [FromQuery] int groupId,
   [FromQuery] int? projectId,
   [FromQuery] string lotNo,
   [FromQuery] int? processId)
        {
            try
            {
                // Step 1: Get transactions with Status != 2
                var transactionDetailsQuery = _context.Transaction
                    .Where(t => t.Status != 2)
                    .Select(t => new { t.TransactionId, t.QuantitysheetId, t.ProcessId });

                if (processId.HasValue)
                    transactionDetailsQuery = transactionDetailsQuery.Where(t => t.ProcessId == processId.Value);

                var transactionDetails = await transactionDetailsQuery.ToListAsync();
                var validTransactionQsIds = transactionDetails.Select(t => t.QuantitysheetId).Distinct().ToList();

                // Step 2: Get QuantitySheets
                var quantitySheetsQuery = _context.QuantitySheets
                    .Where(q => q.Status == 1 && validTransactionQsIds.Contains(q.QuantitySheetId) && !string.IsNullOrEmpty(q.LotNo));

                if (projectId.HasValue)
                    quantitySheetsQuery = quantitySheetsQuery.Where(q => q.ProjectId == projectId.Value);

                if (!string.IsNullOrEmpty(lotNo))
                    quantitySheetsQuery = quantitySheetsQuery.Where(q => q.LotNo == lotNo);

                var quantitySheets = await quantitySheetsQuery
                    .Select(q => new
                    {
                        q.QuantitySheetId,
                        q.ProjectId,
                        q.LotNo,
                        q.Quantity,
                        q.CatchNo
                    }).ToListAsync();

                // Step 3: Get Dispatches
                var dispatchesQuery = _context.Dispatch
                    .Where(d => !string.IsNullOrEmpty(d.LotNo));

                if (projectId.HasValue)
                    dispatchesQuery = dispatchesQuery.Where(d => d.ProjectId == projectId.Value);

                if (!string.IsNullOrEmpty(lotNo))
                    dispatchesQuery = dispatchesQuery.Where(d => d.LotNo == lotNo);

                var dispatches = await dispatchesQuery
                    .Select(d => new { d.ProjectId, d.LotNo })
                    .ToListAsync();

                // Step 4: Filter not dispatched
                var pendingSheets = quantitySheets
                    .Where(qs => !dispatches.Any(d => d.ProjectId == qs.ProjectId && d.LotNo.Equals(qs.LotNo, StringComparison.OrdinalIgnoreCase)))
                    .ToList();

                // Step 5: Project Group mapping + TypeId
                var projectIds = pendingSheets.Select(qs => qs.ProjectId).Distinct().ToList();
                var projectDetails = await _context.Projects
                    .Where(p => projectIds.Contains(p.ProjectId) && p.GroupId == groupId)
                    .Select(p => new { p.ProjectId, p.GroupId, p.TypeId })
                    .ToDictionaryAsync(p => p.ProjectId, p => new { p.GroupId, p.TypeId });

                // Step 6: Join pending sheets with transactionDetails by QuantitysheetId and ProcessId
                var matchedData = pendingSheets
                    .SelectMany(qs =>
                        transactionDetails
                            .Where(t => t.QuantitysheetId == qs.QuantitySheetId)
                            .Select(t => new
                            {
                                QuantitySheetId = qs.QuantitySheetId,
                                LotNo = qs.LotNo,
                                Quantity = qs.Quantity,
                                CatchNo = qs.CatchNo,
                                ProcessId = t.ProcessId,
                                ProjectId = qs.ProjectId,
                                GroupId = projectDetails.TryGetValue(qs.ProjectId, out var proj) ? proj.GroupId : (int?)null,
                                TypeId = projectDetails.TryGetValue(qs.ProjectId, out var proj2) ? proj2.TypeId : (int?)null
                            })
                    )
                    .Where(x => x.GroupId.HasValue && (!processId.HasValue || x.ProcessId == processId.Value))
                    .ToList();

                var groupedData = matchedData
                    .GroupBy(x => new { x.ProjectId, x.LotNo, x.ProcessId, x.GroupId, x.TypeId });

                // Step 7: Final projection with EventLog
                var result = new List<object>();

                foreach (var g in groupedData)
                {
                    var qsIds = g.Select(x => x.QuantitySheetId).ToList();
                    var processIdInGroup = g.Key.ProcessId;

                    var transIds = transactionDetails
                        .Where(t => qsIds.Contains(t.QuantitysheetId) && t.ProcessId == processIdInGroup)
                        .Select(t => t.TransactionId)
                        .Distinct()
                        .ToList();

                    int? maxTransId = transIds.Any() ? transIds.Max() : null;

                    DateTime? lastLoggedAt = null;

                    if (maxTransId.HasValue)
                    {
                        lastLoggedAt = await _context.EventLogs
                            .Where(e => e.TransactionId == maxTransId.Value)
                            .OrderByDescending(e => e.LoggedAT)
                            .Select(e => (DateTime?)e.LoggedAT)
                            .FirstOrDefaultAsync();
                    }

                    result.Add(new
                    {
                        ProjectId = g.Key.ProjectId,
                        LotNo = g.Key.LotNo,
                        ProcessId = g.Key.ProcessId,
                        TypeId = g.Key.TypeId,
                        TotalCatchCount = g.Count(),
                        TotalQuantity = g.Sum(x => x.Quantity),
                        LastLoggedAt = lastLoggedAt,
                        CatchDetails = processId.HasValue ? g.Select(item => new
                        {
                            CatchNo = item.CatchNo,
                            Quantity = item.Quantity
                        }).ToList() : null
                    });
                }

                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "An error occurred", error = ex.Message });
            }
        }


        //[HttpGet("UnderProduction")]
        //public async Task<IActionResult> GetUnderProduction()
        //{
        //    // Step 1: Fetch all required data from the database
        //    var getProject = await _context.Projects
        //        .Where(p => p.ProjectId >= 100)
        //        .Select(p => new { p.ProjectId, p.Name, p.GroupId, p.TypeId })
        //        .ToListAsync();

        //    var getdistinctlotsofproject = await _context.QuantitySheets
        //        .Where(q => q.Status == 1)
        //        .Select(q => new { q.LotNo, q.ProjectId, q.ExamDate, q.QuantitySheetId, q.Quantity })
        //        .Distinct()
        //        .ToListAsync();



        //    var getdispatchedlots = await _context.Dispatch
        //        .Select(d => new { d.LotNo, d.ProjectId })
        //        .ToListAsync();
        //    var dispatchedLotKeys = new HashSet<string>(
        //        getdispatchedlots.Select(d => $"{d.ProjectId}|{d.LotNo}")
        //    );

        //    var quantitySheetGroups = getdistinctlotsofproject
        //        .GroupBy(q => new { q.LotNo, q.ProjectId })
        //        .ToDictionary(
        //            g => $"{g.Key.ProjectId}|{g.Key.LotNo}",
        //            g => new {
        //                TotalCatchNo = g.Select(q => q.QuantitySheetId).Count(),
        //                TotalQuantity = g.Sum(q => q.Quantity),
        //                FromDate = g.Min(q => DateTime.TryParse(q.ExamDate, out var d) ? d : DateTime.MinValue),
        //                ToDate = g.Max(q => DateTime.TryParse(q.ExamDate, out var d) ? d : DateTime.MinValue)
        //            }
        //              );



        //    // Step 3: Perform joins and calculate result in-memory
        //    var underProduction = (from project in getProject
        //                           from kvp in quantitySheetGroups
        //                           let keyParts = kvp.Key.Split(new[] { '|' }, StringSplitOptions.None)
        //                           let projectId = int.Parse(keyParts[0])
        //                           let lotNo = keyParts[1]
        //                           where project.ProjectId == projectId && !dispatchedLotKeys.Contains(kvp.Key)
        //                           select new
        //                           {
        //                               project.ProjectId,
        //                               project.Name,
        //                               project.GroupId,
        //                               FromDate = kvp.Value.FromDate,
        //                               ToDate = kvp.Value.ToDate,
        //                               project.TypeId,
        //                               LotNo = lotNo,
        //                               TotalCatchNo = kvp.Value.TotalCatchNo,
        //                               TotalQuantity = kvp.Value.TotalQuantity
        //                           }).ToList();

        //    return Ok(underProduction);
        //}

      [HttpGet("UnderProduction")]
public async Task<IActionResult> GetUnderProduction()
{
    var today = DateTime.Today;

    var getProject = await _context.Projects
        .Where(p => p.ProjectId >= 88)
        .Select(p => new { p.ProjectId, p.Name, p.GroupId, p.TypeId })
        .ToListAsync();

    var getdistinctlotsofproject = await _context.QuantitySheets
        .Where(q => q.Status == 1)
        .Select(q => new { q.LotNo, q.ProjectId, q.ExamDate, q.QuantitySheetId, q.Quantity })
        .ToListAsync();

    var getdispatchedlots = await _context.Dispatch
        .Select(d => new { d.ProjectId, d.LotNo, d.Status, d.DispatchDate })
        .ToListAsync();

    var dispatchedDict = getdispatchedlots
        .ToDictionary(d => $"{d.ProjectId}|{d.LotNo}", d => d);

    var quantitySheetGroups = getdistinctlotsofproject
        .GroupBy(q => new { q.ProjectId, q.LotNo })
        .ToDictionary(
            g => $"{g.Key.ProjectId}|{g.Key.LotNo}",
            g => new
            {
                TotalCatchNo = g.Count(),
                TotalQuantity = g.Sum(q => q.Quantity),
                FromDate = g.Min(q => DateTime.TryParse(q.ExamDate, out var d) ? d : DateTime.MinValue),
                ToDate = g.Max(q => DateTime.TryParse(q.ExamDate, out var d) ? d : DateTime.MinValue)
            });

    var underProduction = quantitySheetGroups
        .Select(kvp =>
        {
            var keyParts = kvp.Key.Split('|');
            var projectId = int.Parse(keyParts[0]);
            var lotNo = keyParts[1];
            var dispatchKey = $"{projectId}|{lotNo}";
            dispatchedDict.TryGetValue(dispatchKey, out var dispatch);

            return new
            {
                Project = getProject.FirstOrDefault(p => p.ProjectId == projectId),
                LotNo = lotNo,
                Dispatch = dispatch,
                GroupData = kvp.Value
            };
        })
        .Where(x =>
            x.Project != null &&
            (
                x.Dispatch == null || // no dispatch
                (!x.Dispatch.Status &&  // status = 0 (pending)
                 x.Dispatch.DispatchDate.HasValue
                 ) // dispatch date in future
            )
        )
        .Select(x => new
        {
            x.Project.ProjectId,
            x.Project.Name,
            x.Project.GroupId,
            x.Project.TypeId,
            LotNo = x.LotNo,
            FromDate = x.GroupData.FromDate,
            ToDate = x.GroupData.ToDate,
            TotalCatchNo = x.GroupData.TotalCatchNo,
            TotalQuantity = x.GroupData.TotalQuantity
        })
        .ToList();

    return Ok(underProduction);
}

    }
}