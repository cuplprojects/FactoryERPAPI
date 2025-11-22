using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ERPAPI.Data;
using ERPAPI.Model;
using Microsoft.CodeAnalysis;
using System.Diagnostics;
using ERPAPI.Services;
using ERPAPI.Service.ProjectTransaction;
using ERPAPI.Service;


namespace ERPAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class TransactionsController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly ILoggerService _loggerService;
       
        private readonly IProjectTransactionService _projectTransactionService;
        private readonly ProjectService.Services.ProjectService _projectService;

        public TransactionsController(AppDbContext context,  IProjectTransactionService projectTransactionService, ILoggerService loggerService, 
            ProjectService.Services.ProjectService projectService)
        {
            _context = context;
            _projectService = projectService;
          
            _projectTransactionService = projectTransactionService;
            _loggerService = loggerService;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<object>>> GetTransaction(int projectId, int processId)
        {
            // Fetch the transactions based on projectId and processId
            var transactions = await (from t in _context.Transaction
                                      where t.ProjectId == projectId && t.ProcessId == processId
                                      select t).ToListAsync();

            if (transactions == null || !transactions.Any())
            {
                return NotFound(); // Return a 404 if no transactions are found
            }

            // Now, we will get the users based on the teamId in each transaction
            var transactionsWithUsers = transactions
                .Select(t =>
                {
                    var parsedAlarmId = TryParseAlarmId(t.AlarmId); // Apply parsing here
                    var alarm = parsedAlarmId is int parsedId
                        ? _context.Alarm.FirstOrDefault(a => a.AlarmId == parsedId)
                        : null;

                    // Fetch the user names based on teamId (which is an array of userId)
                    var userNames = _context.Users
                        .Where(u => t.TeamId.Contains(u.UserId)) // Match userId with the ids in TeamId
                        .Select(u => u.FirstName + "" + u.LastName)
                        .ToList();

                    return new
                    {
                        t.TransactionId,
                        t.AlarmId,
                        t.ZoneId,
                        t.QuantitysheetId,
                        t.TeamId,
                        TeamUserNames = userNames, // Add the list of usernames here
                        t.Remarks,
                        t.LotNo,
                        t.InterimQuantity,
                        t.ProcessId,
                        t.VoiceRecording,
                        t.Status,
                        t.MachineId,
                        AlarmMessage = alarm != null ? alarm.Message : t.AlarmId // Handle null case for alarms
                    };
                }).ToList(); // Apply the transformation in memory

            return Ok(transactionsWithUsers); // Return the modified transactions with user names
        }



        [HttpGet("GetProjectTransactionsDataOld")]
        public async Task<ActionResult<IEnumerable<object>>> GetProjectTransactionsDataOld(int projectId, int processId)
        {
            // Fetch quantity sheet data
            var quantitySheetData = await _context.QuantitySheets
                .Where(q => q.ProjectId == projectId && q.Status == 1 && q.StopCatch == 0)
                .ToListAsync();

            // Fetch transaction data and parse alarm messages if needed
            var transactions = await (from t in _context.Transaction
                                      where t.ProjectId == projectId && t.ProcessId == processId
                                      select new
                                      {
                                          t.TransactionId,
                                          t.AlarmId,
                                          t.ZoneId,
                                          t.QuantitysheetId,
                                          t.TeamId,
                                          t.Remarks,
                                          t.LotNo,
                                          t.InterimQuantity,
                                          t.ProcessId,
                                          t.VoiceRecording,
                                          t.Status,
                                          t.MachineId
                                      }).ToListAsync();

            // Fetch alarm messages
            var alarms = await _context.Alarm.ToListAsync();

            // Fetch project details
            var project = await _context.Projects.FirstOrDefaultAsync(p => p.ProjectId == projectId);

            // Fetch users for all team members in advance to minimize the number of queries
            var allUsers = await _context.Users.ToListAsync();
            var allZone = await _context.Zone.ToListAsync();
            var allMachine = await _context.Machine.ToListAsync();

            // Map transactions with their alarm messages and usernames
            var transactionsWithAlarms = transactions.Select(t =>
            {
                var parsedAlarmId = TryParseAlarmId(t.AlarmId);
                var alarm = parsedAlarmId is int parsedId
                    ? alarms.FirstOrDefault(a => a.AlarmId == parsedId)
                    : null;

                // Get the usernames for each userId in the TeamId array
                var userNames = allUsers
                    .Where(u => t.TeamId.Contains(u.UserId))
                    .Select(u => u.FirstName + " " + u.LastName)
                    .ToList();

                var zone = allZone.FirstOrDefault(z => z.ZoneId == t.ZoneId);
                var zoneNo = zone != null ? zone.ZoneNo : null;

                var machine = allMachine.FirstOrDefault(z => z.MachineId == t.MachineId);
                var machinename = machine != null ? machine.MachineName : null;

                return new
                {
                    t.TransactionId,
                    AlarmId = t.AlarmId,
                    ZoneId = t.ZoneId,
                    zoneNo = zoneNo,
                    machinename = machinename,
                    QuantitysheetId = t.QuantitysheetId,
                    TeamId = t.TeamId,
                    Remarks = t.Remarks,
                    LotNo = t.LotNo,
                    TeamUserNames = userNames,  // Include the usernames
                    InterimQuantity = t.InterimQuantity,
                    ProcessId = t.ProcessId,
                    VoiceRecording = t.VoiceRecording,
                    Status = t.Status,
                    MachineId = t.MachineId,
                    AlarmMessage = alarm != null ? alarm.Message : t.AlarmId, // Handle null case for alarms
                    
                };
            }).ToList();

            // Apply the logic for SeriesName from Project (using Project.SeriesName)
            var quantitySheetDataWithSeriesName = quantitySheetData
                .GroupBy(q => q.CatchNo)  // Group by CatchNo for SeriesName assignment
                .Select(group =>
                {
                    var seriesName = project?.SeriesName ?? "";  // Get the SeriesName from the project

                    // Assign SeriesName based on the group
                    var quantitySheetsWithSeriesName = group.Select((q, index) =>
                    {
                        var seriesLetter = index < seriesName.Length ? seriesName[index].ToString() : ""; // Use SeriesName from project
                        return new
                        {
                            q.QuantitySheetId,
                            q.ProjectId,
                            q.LotNo,
                            q.CatchNo,
                            q.Paper,
                            q.ExamDate,
                            q.ExamTime,
                            q.Course,
                            q.Subject,
                            q.InnerEnvelope,
                            q.OuterEnvelope,
                            q.Quantity,
                            q.Pages,
                            q.PercentageCatch,
                            
                            SeriesName = seriesLetter,  // Assign the SeriesName here
                            ProcessIds = q.ProcessId,   // Assuming ProcessIds is a list, map it directly
                        };
                    }).ToList();

                    // Return the group with the SeriesName applied
                    return new
                    {
                        CatchNo = group.Key,  // The CatchNo for this group
                        QuantitySheets = quantitySheetsWithSeriesName
                    };
                })
                .ToList();

            // Flatten the grouped data into a single list
            var flattenedQuantitySheetData = quantitySheetDataWithSeriesName
                .SelectMany(group => group.QuantitySheets)
                .ToList();

            // Combine QuantitySheet and Transaction data in a structured response
            var responseData = flattenedQuantitySheetData.Select(q =>
            {
                return new
                {
                    q.QuantitySheetId,
                    q.ProjectId,
                    q.LotNo,
                    q.CatchNo,
                    q.Paper,
                    q.ExamDate,
                    q.ExamTime,
                    q.Course,
                    q.Subject,
                    q.Pages,
                    q.InnerEnvelope,
                    q.OuterEnvelope,
                    q.Quantity,
                    q.PercentageCatch,
                    q.SeriesName,  // Directly use the SeriesName
                    
                    ProcessIds = q.ProcessIds, // Assuming ProcessIds is a list, map it directly
                    Transactions = transactionsWithAlarms
                        .Where(t => t.QuantitysheetId == q.QuantitySheetId) // Only transactions matching the QuantitySheetId
                        .ToList()
                };
            }).ToList();

            return Ok(responseData);
        }



        [HttpGet("GetProjectTransactionsData")]
        public async Task<ActionResult<IEnumerable<object>>> GetProjectTransactionsData(int projectId, int processId)
        {
            try
            {
                // Call the service method to get the data
                var projectTransactionsData = await _projectTransactionService.GetProjectTransactionsDataAsync(projectId, processId);

                // Return the data as a successful response
                return Ok(projectTransactionsData);
            }
            catch (System.Exception ex)
            {
                // In case of any error, return a bad request response with the exception message
                return BadRequest(new { message = ex.Message });
            }
        }
        // Utility function to attempt parsing AlarmId and return an integer if possible, else return the original value
        private object TryParseAlarmId(object alarmId)
        {
            if (alarmId == null)
            {
                return null; // Return null if AlarmId is null
            }

            int parsedId;
            if (int.TryParse(alarmId.ToString(), out parsedId))
            {
                return parsedId; // Return integer if parsing succeeds
            }

            return alarmId; // Return the original value if parsing fails
        }
        // Utility function to attempt parsing AlarmId and return an integer if possible, else return the original value






        //GET: api/Transactions/5
        [HttpGet("{id}")]
        public async Task<ActionResult<Transaction>> GetTransaction(int id)
        {
            var transaction = await _context.Transaction.FindAsync(id);

            if (transaction == null)
            {
                return NotFound();
            }

            return transaction;
        }



        // PUT: api/Transactions/5
        [HttpPut("{id}")]
        public async Task<IActionResult> PutTransaction(int id, Transaction transaction)
        {
            if (id != transaction.TransactionId)
            {
                return BadRequest();
            }

            // Retrieve the existing transaction from the database
            var existingTransaction = await _context.Transaction
                .AsNoTracking() // Ensures the retrieved entity is not tracked to avoid conflicts during updates
                .FirstOrDefaultAsync(t => t.TransactionId == id);

            if (existingTransaction == null)
            {
                return NotFound();
            }

            // Capture the old status before the update
            var oldStatus = existingTransaction.Status;

            // Update the transaction in the context
            _context.Entry(transaction).State = EntityState.Modified;

            try
            {
                // Save changes to the database
                await _context.SaveChangesAsync();

                // Log the update with old and new status
                _loggerService.LogEventWithTransaction(
                    "Transaction updated",
                    "Transaction",
                    User.Identity?.Name != null ? int.Parse(User.Identity.Name) : 0, // Replace with the current user's ID or a dynamic ID
                    id,
                    oldValue: oldStatus.ToString(),
                    newValue: transaction.Status.ToString()
                );
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!TransactionExists(id))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }

            return NoContent();
        }



        [HttpPut("quantitysheet/{quantitysheetId}")]
        public async Task<IActionResult> PutTransactionId(int quantitysheetId, Transaction transaction)
        {
            if (quantitysheetId != transaction.QuantitysheetId)
            {
                return BadRequest();
            }

            // Retrieve the existing transaction from the database
            var existingTransaction = await _context.Transaction
                .AsNoTracking() // Ensures the entity is not tracked to avoid conflicts during updates
                .FirstOrDefaultAsync(t => t.QuantitysheetId == quantitysheetId);

            if (existingTransaction == null)
            {
                return NotFound();
            }

            // Capture the old status before the update
            var oldStatus = existingTransaction.Status;

            // Update the transaction in the context
            _context.Entry(transaction).State = EntityState.Modified;

            try
            {
                // Save changes to the database
                await _context.SaveChangesAsync();

                // Log the update with old and new status
                _loggerService.LogEventWithTransaction(
                    message: "Transaction updated",
                    category: "Transaction",
                    triggeredBy: User.Identity?.Name != null ? int.Parse(User.Identity.Name) : 0, // Replace with the current user's ID or a dynamic ID
                    transactionId: existingTransaction.TransactionId,
                    oldValue: oldStatus.ToString(),
                    newValue: transaction.Status.ToString()
                );
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!TransactionExists(quantitysheetId))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }

            return NoContent();
        }

        // TransactionController.cs
        [HttpPost]
        public async Task<IActionResult> CreateTransaction([FromBody] Transaction transaction)
        {
            if (transaction == null)
            {
                return BadRequest("Invalid data.");
            }

            // Fetch the Process from the Process table using ProcessId
            var process = await _context.Processes
                .FirstOrDefaultAsync(p => p.Id == transaction.ProcessId);

            if (process == null)
            {
                return BadRequest("Invalid ProcessId.");
            }

            var validProcessNames = new List<string> { "Digital Printing", "CTP", "Offset Printing", "Cutting" };

            if (validProcessNames.Contains(process.Name))
            {
                var existingTransaction = await _context.Transaction
                    .FirstOrDefaultAsync(t => t.QuantitysheetId == transaction.QuantitysheetId &&
                                              t.LotNo == transaction.LotNo &&
                                              t.ProcessId == transaction.ProcessId);

                if (existingTransaction != null)
                {
                    var oldValues = existingTransaction.GetType().GetProperties()
                        .ToDictionary(prop => prop.Name, prop =>
                            prop.Name == "TeamId"
                                ? string.Join(",", existingTransaction.TeamId ?? new List<int>())
                                : prop.GetValue(existingTransaction)?.ToString());

                    existingTransaction.InterimQuantity = transaction.InterimQuantity;
                    existingTransaction.Remarks = transaction.Remarks;
                    existingTransaction.VoiceRecording = transaction.VoiceRecording;
                    existingTransaction.ZoneId = transaction.ZoneId;
                    existingTransaction.MachineId = transaction.MachineId;
                    existingTransaction.Status = transaction.Status;
                    existingTransaction.AlarmId = transaction.AlarmId;
                    existingTransaction.TeamId = transaction.TeamId ?? new List<int>();

                    var newValues = existingTransaction.GetType().GetProperties()
                        .ToDictionary(prop => prop.Name, prop =>
                            prop.Name == "TeamId"
                                ? string.Join(",", existingTransaction.TeamId ?? new List<int>())
                                : prop.GetValue(existingTransaction)?.ToString());

                    _context.Transaction.Update(existingTransaction);

                    foreach (var key in oldValues.Keys)
                    {
                        var oldValue = oldValues[key];
                        var newValue = newValues[key];

                        if (oldValue != null && newValue != null && oldValue != newValue)
                        {
                            _loggerService.LogEventWithTransaction(
                                $"{key} updated",
                                "Transaction",
                                User.Identity?.Name != null ? int.Parse(User.Identity.Name) : 0,
                                existingTransaction.TransactionId,
                                oldValue: oldValue,
                                newValue: newValue
                            );
                        }
                        else if (oldValue == null && newValue != null)
                        {
                            _loggerService.LogEventWithTransaction(
                                $"{key} added",
                                "Transaction",
                                User.Identity?.Name != null ? int.Parse(User.Identity.Name) : 0,
                                existingTransaction.TransactionId,
                                newValue: newValue
                            );
                        }
                    }
                }
                else
                {
                    transaction.TeamId = transaction.TeamId ?? new List<int>(); // Ensure TeamId is not null
                    _context.Transaction.Add(transaction);

                    await _context.SaveChangesAsync();

                    _loggerService.LogEventWithTransaction(
                        "Transaction created",
                        "Transaction",
                        User.Identity?.Name != null ? int.Parse(User.Identity.Name) : 0,
                        transaction.TransactionId,
                        newValue: $"TeamId: {string.Join(",", transaction.TeamId)}, ZoneId: {transaction.ZoneId}, MachineId: {transaction.MachineId}"
                    );
                }

                await _context.SaveChangesAsync();

                return Ok(new { message = "Transaction created/updated successfully." });
            }
            else
            {
                var quantitySheet = await _context.QuantitySheets
                    .FirstOrDefaultAsync(qs => qs.QuantitySheetId == transaction.QuantitysheetId);

                if (quantitySheet == null)
                {
                    return BadRequest("QuantitySheet not found.");
                }

                string catchNumber = quantitySheet.CatchNo;

                var quantitySheets = await _context.QuantitySheets
                    .Where(qs => qs.CatchNo == catchNumber && qs.LotNo == transaction.LotNo.ToString() && qs.ProjectId == transaction.ProjectId)
                    .ToListAsync();

                if (quantitySheets == null || !quantitySheets.Any())
                {
                    return BadRequest("No matching QuantitySheets found.");
                }

                foreach (var sheet in quantitySheets)
                {
                    var existingTransaction = await _context.Transaction
                        .FirstOrDefaultAsync(t => t.QuantitysheetId == sheet.QuantitySheetId &&
                                                  t.LotNo == transaction.LotNo &&
                                                  t.ProcessId == transaction.ProcessId);

                    if (existingTransaction != null)
                    {
                        var oldValues = existingTransaction.GetType().GetProperties()
                            .Where(prop => prop.GetValue(existingTransaction) != null)
                            .ToDictionary(prop => prop.Name, prop =>
                                prop.Name == "TeamId"
                                    ? string.Join(",", existingTransaction.TeamId ?? new List<int>())
                                    : prop.GetValue(existingTransaction)?.ToString());

                        existingTransaction.InterimQuantity = transaction.InterimQuantity;
                        existingTransaction.Remarks = transaction.Remarks;
                        existingTransaction.VoiceRecording = transaction.VoiceRecording;
                        existingTransaction.ZoneId = transaction.ZoneId;
                        existingTransaction.MachineId = transaction.MachineId;
                        existingTransaction.Status = transaction.Status;
                        existingTransaction.AlarmId = transaction.AlarmId;
                        existingTransaction.TeamId = transaction.TeamId ?? new List<int>();

                        var newValues = existingTransaction.GetType().GetProperties()
                            .Where(prop => prop.GetValue(existingTransaction) != null)
                            .ToDictionary(prop => prop.Name, prop =>
                                prop.Name == "TeamId"
                                    ? string.Join(",", existingTransaction.TeamId ?? new List<int>())
                                    : prop.GetValue(existingTransaction)?.ToString());

                        _context.Transaction.Update(existingTransaction);

                        foreach (var key in oldValues.Keys)
                        {
                            var oldValue = oldValues[key];
                            var newValue = newValues[key];

                            if (oldValue != null && newValue != null && oldValue != newValue)
                            {
                                _loggerService.LogEventWithTransaction(
                                    $"{key} updated",
                                    "Transaction",
                                    User.Identity?.Name != null ? int.Parse(User.Identity.Name) : 0,
                                    existingTransaction.TransactionId,
                                    oldValue: oldValue,
                                    newValue: newValue
                                );
                            }
                            else if (oldValue == null && newValue != null)
                            {
                                _loggerService.LogEventWithTransaction(
                                    $"{key} added",
                                    "Transaction",
                                    User.Identity?.Name != null ? int.Parse(User.Identity.Name) : 0,
                                    existingTransaction.TransactionId,
                                    newValue: newValue
                                );
                            }
                        }
                    }
                    else
                    {
                        var newTransaction = new Transaction
                        {
                            InterimQuantity = transaction.InterimQuantity,
                            Remarks = transaction.Remarks,
                            VoiceRecording = transaction.VoiceRecording,
                            ProjectId = transaction.ProjectId,
                            QuantitysheetId = sheet.QuantitySheetId,
                            ProcessId = transaction.ProcessId,
                            ZoneId = transaction.ZoneId,
                            MachineId = transaction.MachineId,
                            Status = transaction.Status,
                            AlarmId = transaction.AlarmId,
                            LotNo = transaction.LotNo,
                            TeamId = transaction.TeamId ?? new List<int>()
                        };

                        _context.Transaction.Add(newTransaction);

                        await _context.SaveChangesAsync();

                        var addedValues = newTransaction.GetType().GetProperties()
                            .Where(prop => prop.GetValue(newTransaction) != null)
                            .ToDictionary(prop => prop.Name, prop =>
                                prop.Name == "TeamId"
                                    ? string.Join(",", newTransaction.TeamId ?? new List<int>())
                                    : prop.GetValue(newTransaction)?.ToString());

                        foreach (var key in addedValues.Keys)
                        {
                            _loggerService.LogEventWithTransaction(
                                $"{key} added",
                                "Transaction",
                                User.Identity?.Name != null ? int.Parse(User.Identity.Name) : 0,
                                newTransaction.TransactionId,
                                newValue: addedValues[key]
                            );
                        }
                    }
                }

                await _context.SaveChangesAsync();

                return Ok(new { message = "Transactions created/updated successfully." });
            }
        }










        // DELETE: api/Transactions/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteTransaction(int id)
        {
            var transaction = await _context.Transaction.FindAsync(id);
            if (transaction == null)
            {
                return NotFound();
            }

            _context.Transaction.Remove(transaction);
            await _context.SaveChangesAsync();

            return NoContent();
        }
        private bool TransactionExists(int id)
        {
            return _context.Transaction.Any(e => e.TransactionId == id);
        }



        /*[HttpGet("all-project-completion-percentages")]
        public async Task<ActionResult> GetAllProjectCompletionPercentages()
        {
            var projectCompletionPercentages = await _projectCompletionService.CalculateProjectCompletionPercentages();
            return Ok(projectCompletionPercentages);
        }*/

        [HttpGet("all-project-completion-percentages")]
        public async Task<ActionResult> GetAllProjectCompletionPercentages([FromQuery] int userId, [FromQuery] int page = 1, [FromQuery] int pageSize = 5, [FromQuery] int? starredProjectId = null)
        {
            // Step 1: Fetch Projects Assigned to the User
            var userProjects = await _projectService.GetDistinctProjectsForUser(userId);

            if (userProjects == null || !userProjects.Any())
            {
                return NotFound("No projects assigned to this user.");
            }

            var allProjectIds = userProjects.Select(p => p.ProjectId).ToList();
            var projectIds = new List<int>();

            // Step 2: Handle Starred Project Priority
            if (starredProjectId.HasValue && allProjectIds.Contains(starredProjectId.Value))
            {
                // Add starred project first (only on first page)
                if (page == 1)
                {
                    projectIds.Add(starredProjectId.Value);
                    //pageSize--; // Reduce page size by 1 to accommodate starred project
                }

                // Remove starred project from the list to avoid duplicates
                allProjectIds.Remove(starredProjectId.Value);
            }

            // Step 3: Apply Pagination to Remaining Projects
            var remainingProjectIds = allProjectIds.Skip((page - 1) * pageSize).Take(pageSize).ToList();
            projectIds.AddRange(remainingProjectIds);

            if (!projectIds.Any())
            {
                return NotFound("No more projects available.");
            }

            var projectCompletionPercentages = new List<object>();

            // Step 3: Calculate Completion Percentages
            foreach (var projectId in projectIds)
            {
                var project = await _context.Projects.FirstOrDefaultAsync(p => p.ProjectId == projectId);
                if (project == null)
                {
                    continue;
                }

                var projectProcesses = await _context.ProjectProcesses
                    .Where(p => p.ProjectId == projectId)
                    .ToListAsync();

                var quantitySheets = await _context.QuantitySheets
                    .Where(p => p.ProjectId == projectId)
                    .ToListAsync();

                var transactions = await _context.Transaction
                    .Where(t => t.ProjectId == projectId)
                    .ToListAsync();

                var dispatches = await _context.Dispatch
                    .Where(d => d.ProjectId == projectId)
                    .ToListAsync();

                var totalLotPercentages = new Dictionary<string, double>();
                var lotQuantities = new Dictionary<string, double>();
                double projectTotalQuantity = 0;

                foreach (var quantitySheet in quantitySheets)
                {
                    if (quantitySheet == null || quantitySheet.LotNo == null)
                    {
                        continue; // Skip invalid quantity sheets
                    }
                    var lotNumber = quantitySheet.LotNo.ToString();
                    var processIdWeightage = new Dictionary<int, double>();
                    double totalWeightageSum = 0;

                    foreach (var processId in quantitySheet.ProcessId)
                    {
                        var process = projectProcesses.FirstOrDefault(p => p.ProcessId == processId);
                        if (process != null)
                        {
                            processIdWeightage[processId] = Math.Round(process.Weightage, 2);
                            totalWeightageSum += process.Weightage;
                        }
                    }

                    if (totalWeightageSum < 100)
                    {
                        double deficit = 100 - totalWeightageSum;
                        double adjustment = deficit / processIdWeightage.Count;

                        foreach (var key in processIdWeightage.Keys.ToList())
                        {
                            processIdWeightage[key] = Math.Round(processIdWeightage[key] + adjustment, 2);
                        }
                    }

                    double completedWeightageSum = 0;
                    foreach (var kvp in processIdWeightage)
                    {
                        var processId = kvp.Key;
                        var weightage = kvp.Value;

                        var completedProcess = transactions
                            .Any(t => t.QuantitysheetId == quantitySheet.QuantitySheetId
                                      && t.ProcessId == processId
                                      && t.Status == 2);

                        if (completedProcess)
                        {
                            completedWeightageSum += weightage;
                        }
                    }

                    if (quantitySheet.ProcessId.Contains(14))
                    {
                        var dispatch = dispatches.FirstOrDefault(d => d.LotNo == quantitySheet.LotNo && d.ProcessId == 14);
                        if (dispatch != null)
                        {
                            completedWeightageSum += processIdWeightage[14];
                        }
                    }

                    double lotPercentage = Math.Round(quantitySheet.PercentageCatch * (completedWeightageSum / 100), 2);

                    totalLotPercentages[lotNumber] = Math.Round(
                        totalLotPercentages.GetValueOrDefault(lotNumber) + lotPercentage, 2);

                    lotQuantities[lotNumber] = lotQuantities.GetValueOrDefault(lotNumber) + quantitySheet.Quantity;
                    projectTotalQuantity += quantitySheet.Quantity;
                }

                double totalProjectLotPercentage = 0;

                foreach (var lot in totalLotPercentages)
                {
                    var lotNumber = lot.Key;
                    var quantity = lotQuantities[lotNumber];
                    var lotWeightage = projectTotalQuantity > 0 ? (quantity / projectTotalQuantity) * 100 : 0;

                    totalProjectLotPercentage += totalLotPercentages[lotNumber] * (lotWeightage / 100);
                }

                totalProjectLotPercentage = Math.Round(totalProjectLotPercentage, 2);

                projectCompletionPercentages.Add(new
                {
                    projectName = project.Name,
                    projectId = project.ProjectId,
                    completionPercentage = totalProjectLotPercentage,
                    projectTotalQuantity = projectTotalQuantity
                });
            }

            return Ok(projectCompletionPercentages);
        }



        [HttpGet("alarms")]
        public async Task<ActionResult<IEnumerable<object>>> GetAlarmsByProjectId(int projectId)
        {
            // Fetch alarms that belong to the specified projectId where AlarmId != "0" and not an empty string
            var alarms = await (from t in _context.Transaction
                                join q in _context.QuantitySheets on t.QuantitysheetId equals q.QuantitySheetId into qtyJoin
                                from q in qtyJoin.DefaultIfEmpty() // Left join to handle cases where there's no matching Quantity
                                join p in _context.Processes on t.ProcessId equals p.Id into processjoin
                                from p in processjoin.DefaultIfEmpty()
                                join a in _context.Alarm on t.AlarmId equals a.AlarmId.ToString() into alarmJoin
                                from a in alarmJoin.DefaultIfEmpty() // Left join to handle cases where there's no matching Alarm
                                where t.ProjectId == projectId && t.AlarmId != "0" && !string.IsNullOrEmpty(t.AlarmId)
                                select new
                                {
                                    t.TransactionId,
                                    t.AlarmId,
                                    t.MachineId,
                                    t.InterimQuantity,
                                    t.TeamId,
                                    t.ZoneId,
                                    t.ProcessId,
                                    t.QuantitysheetId,
                                    t.ProjectId,
                                    t.LotNo,
                                    Process = p != null ? p.Name : null,
                                    CatchNumber = q != null ? q.CatchNo : null, // Handle null if no matching Quantity
                                    AlarmMessage = a != null ? a.Message : null // Handle null if no matching Alarm
                                }).ToListAsync();

            if (alarms == null || !alarms.Any())
            {
                return NotFound(); // Return 404 if no alarms are found
            }

            return Ok(alarms);
        }



        [HttpGet("combined-percentages")]
        public async Task<ActionResult> GetCombinedPercentages(int projectId)
        {
            var projectProcesses = await _context.ProjectProcesses
                .Where(p => p.ProjectId == projectId)
                .ToListAsync();

            var quantitySheets = await _context.QuantitySheets
                .Where(p => p.ProjectId == projectId && p.StopCatch == 0)
                .ToListAsync();

            var transactions = await _context.Transaction
                .Where(t => t.ProjectId == projectId)
                .ToListAsync();

            var dispatches = await _context.Dispatch
                .Where(d => d.ProjectId == projectId)
                .ToListAsync();

            var lots = new Dictionary<string, Dictionary<int, dynamic>>();
            var totalLotPercentages = new Dictionary<string, double>();
            var lotQuantities = new Dictionary<string, double>();
            var lotWeightages = new Dictionary<string, double>();
            var projectLotPercentages = new Dictionary<string, double>();
            var lotProcessWeightageSum = new Dictionary<string, Dictionary<int, double>>();
            double projectTotalQuantity = 0;

            foreach (var quantitySheet in quantitySheets)
            {
                var processIdWeightage = new Dictionary<int, double>();
                double totalWeightageSum = 0;

                foreach (var processId in quantitySheet.ProcessId)
                {
                    var process = projectProcesses.FirstOrDefault(p => p.ProcessId == processId);
                    if (process != null)
                    {
                        processIdWeightage[processId] = Math.Round(process.Weightage, 2);

                        if (quantitySheet.ProcessId.Contains(processId))
                        {
                            totalWeightageSum += process.Weightage;
                        }
                    }
                }

                if (totalWeightageSum < 100)
                {
                    double deficit = 100 - totalWeightageSum;
                    double adjustment = deficit / processIdWeightage.Count;

                    foreach (var key in processIdWeightage.Keys.ToList())
                    {
                        processIdWeightage[key] = Math.Round(processIdWeightage[key] + adjustment, 2);
                    }

                    totalWeightageSum = processIdWeightage.Values.Sum();
                }

                double completedWeightageSum = 0;
                foreach (var kvp in processIdWeightage)
                {
                    var processId = kvp.Key;
                    var weightage = kvp.Value;

                    var completedProcess = transactions
                        .Any(t => t.QuantitysheetId == quantitySheet.QuantitySheetId
                                  && t.ProcessId == processId
                                  && t.Status == 2);

                    if (completedProcess)
                    {
                        completedWeightageSum += weightage;
                    }
                }

                double lotPercentage = Math.Round(quantitySheet.PercentageCatch * (completedWeightageSum / 100), 2);
                var lotNumber = quantitySheet.LotNo;

                if (!lots.ContainsKey(lotNumber))
                {
                    lots[lotNumber] = new Dictionary<int, dynamic>();
                    totalLotPercentages[lotNumber] = 0;
                    lotQuantities[lotNumber] = 0;
                }

                lots[lotNumber][quantitySheet.QuantitySheetId] = new
                {
                    CompletedProcessPercentage = Math.Round(completedWeightageSum, 2),
                    LotPercentage = lotPercentage,
                    ProcessDetails = processIdWeightage
                };

                totalLotPercentages[lotNumber] = Math.Round(totalLotPercentages[lotNumber] + lotPercentage, 2);
                lotQuantities[lotNumber] += quantitySheet.Quantity;
                projectTotalQuantity += quantitySheet.Quantity;

                if (!lotProcessWeightageSum.ContainsKey(lotNumber))
                {
                    lotProcessWeightageSum[lotNumber] = new Dictionary<int, double>();
                }

                foreach (var processId in processIdWeightage.Keys)
                {
                    var lotNumberStr = lotNumber.ToString();

                    // Filter transactions and quantity sheets for the current processId
                    var filteredTransactions = transactions
                        .Where(t => t.LotNo.ToString() == lotNumberStr && t.ProcessId == processId && t.Status == 2 && t.ProjectId == projectId);

                    var filteredQuantitySheets = quantitySheets
                        .Where(qs => qs.LotNo.ToString() == lotNumberStr && qs.ProcessId.Contains(processId) && qs.ProjectId == projectId);


                    var completedQuantitySheets = filteredTransactions.Count(); //2
                    Console.WriteLine(processId + "completed " + completedQuantitySheets);

                    var totalQuantitySheets = filteredQuantitySheets.Count(); //57


                    // Calculate the percentage completion for the processId
                    double processPercentage = totalQuantitySheets > 0
                        ? Math.Round((double)completedQuantitySheets / totalQuantitySheets * 100, 2)
                        : 0;

                    lotProcessWeightageSum[lotNumber][processId] = processPercentage;

                    // Check Dispatch table for ProcessId 14 and Status 1
                    if (processId == 14)
                    {
                        var dispatch = dispatches.FirstOrDefault(d => d.LotNo == lotNumber && d.ProcessId == 14 && d.Status);
                        if (dispatch != null && dispatch.Status)
                        {
                            lotProcessWeightageSum[lotNumber][processId] = 100;
                        }
                    }
                }

            }
            // Adjust totalLotPercentages if ProcessId 14 is completed

            foreach (var lotNumber in totalLotPercentages.Keys.ToList())
            {
                var process14Completed = lotProcessWeightageSum[lotNumber].ContainsKey(14) &&
                                         lotProcessWeightageSum[lotNumber][14] == 100;
                if (process14Completed)
                {
                    totalLotPercentages[lotNumber] = 100; // Override to 100% if ProcessId 14 is completed
                }
            }

            foreach (var lot in lotQuantities)
            {
                var lotNumber = lot.Key;
                var quantity = lot.Value;

                lotWeightages[lotNumber] = Math.Round((quantity / projectTotalQuantity) * 100, 2);
                projectLotPercentages[lotNumber] = Math.Round(totalLotPercentages[lotNumber] * lotWeightages[lotNumber] / 100, 2);
            }

            double totalProjectLotPercentage = Math.Round(projectLotPercentages.Values.Sum(), 2);
            projectTotalQuantity = Math.Round(projectTotalQuantity, 2);

            return Ok(new
            {
                TotalLotPercentages = totalLotPercentages,
                LotQuantities = lotQuantities,
                LotWeightages = lotWeightages,
                ProjectLotPercentages = projectLotPercentages,
                TotalProjectLotPercentage = totalProjectLotPercentage,
                ProjectTotalQuantity = projectTotalQuantity,
                LotProcessWeightageSum = lotProcessWeightageSum
            });
        }




        public class SheetPercentage
        {
            public int QuantitySheetId { get; set; }
            public double LotPercent { get; set; }
            public double CatchPercent { get; set; }
        }

        public class Percentages
        {
            public Dictionary<string, double> LotPercent { get; set; }
            public List<SheetPercentage> SheetPercentages { get; set; }
            public double ProjectPercent { get; set; }
            public double TotalCatchQuantity { get; set; }
        }

        [HttpGet("process-percentages")]
        public async Task<ActionResult> GetProcessPercentages(int projectId)
        {
            var processes = await _context.ProjectProcesses
                .Where(p => p.ProjectId == projectId)
                .ToListAsync();

            var quantitySheets = await _context.QuantitySheets
                .Where(qs => qs.ProjectId == projectId && qs.StopCatch == 0)
                .ToListAsync();

            var transactions = await _context.Transaction
                .Where(t => t.ProjectId == projectId)
                .ToListAsync();

            var processesList = new List<object>();
            var totalProjectSheets = 0;
            var totalProjectCompletedSheets = 0;

            foreach (var process in processes)
            {
                var uniqueLots = quantitySheets.Select(qs => qs.LotNo).Distinct();
                var lotsList = new List<object>();
                var totalProcessSheets = 0;
                var totalProcessCompletedSheets = 0;

                foreach (var lotNo in uniqueLots)
                {
                    var lotQuantitySheets = quantitySheets.Where(qs => qs.LotNo == lotNo).ToList();
                    var completedSheets = lotQuantitySheets.Count(qs =>
                        transactions.Any(t =>
                            t.QuantitysheetId == qs.QuantitySheetId &&
                            t.ProcessId == process.ProcessId &&
                            t.Status == 2
                        )
                    );

                    // Calculate total sheets for this lot that are related to the process
                    var totalSheets = lotQuantitySheets.Count(sheet => sheet.ProcessId.Contains(process.ProcessId));

                    // If totalSheets is 0, return 100% (since there is nothing to complete)
                    var percentage = totalSheets == 0
                        ? 100
                        : Math.Round((double)completedSheets / totalSheets * 100, 2);

                    totalProcessSheets += totalSheets;
                    totalProcessCompletedSheets += completedSheets;

                    lotsList.Add(new
                    {
                        lotNumber = lotNo,
                        percentage = percentage,
                        totalSheets = totalSheets,
                        completedSheets = completedSheets
                    });
                }

                totalProjectSheets += totalProcessSheets;
                totalProjectCompletedSheets += totalProcessCompletedSheets;

                var overallPercentage = totalProcessSheets > 0
                    ? Math.Round((double)totalProcessCompletedSheets / totalProcessSheets * 100, 2)
                    : 100; // If no total sheets for the process, consider 100%

                processesList.Add(new
                {
                    processId = process.ProcessId,
                    statistics = new
                    {
                        totalLots = lotsList.Count,
                        totalSheets = totalProcessSheets,
                        completedSheets = totalProcessCompletedSheets,
                        overallPercentage = overallPercentage
                    },
                    lots = lotsList
                });
            }

            var overallProjectPercentage = totalProjectSheets > 0
                ? Math.Round((double)totalProjectCompletedSheets / totalProjectSheets * 100, 2)
                : 100; // If no total sheets for the project, consider 100%

            var result = new
            {
                totalProcesses = processes.Count,
                overallProjectPercentage = overallProjectPercentage,
                processes = processesList
            };

            return Ok(result);
        }


        [HttpGet("process-lot-percentages")]
        public async Task<ActionResult> GetProcessLotPercentages(int projectId)
        {
            var processes = await _context.ProjectProcesses
                .Where(p => p.ProjectId == projectId)
                .ToListAsync();

            var quantitySheets = await _context.QuantitySheets
                .Where(qs => qs.ProjectId == projectId && qs.StopCatch == 0)
                .ToListAsync();

            var transactions = await _context.Transaction
                .Where(t => t.ProjectId == projectId)
                .ToListAsync();

            var processesList = new List<object>();
            var totalProjectSheets = 0;
            var totalProjectCompletedSheets = 0;
            var totalProjectQuantity = 0.0; // To track the overall project quantity

            foreach (var process in processes)
            {
                var processQuantitySheets = quantitySheets
    .Where(qs => qs.ProcessId.Contains(process.ProcessId)) // Check if the ProcessId list contains the current ProcessId
    .ToList();
                // Filter sheets for the current process

                var uniqueLots = processQuantitySheets
                    .Select(qs => qs.LotNo)
                    .Distinct();

                var lotsList = new List<object>();
                var totalProcessSheets = 0;
                var totalProcessCompletedSheets = 0;
                var totalProcessQuantity = processQuantitySheets.Sum(qs => qs.Quantity); // Sum quantity for the process

                foreach (var lotNo in uniqueLots)
                {
                    var lotQuantitySheets = processQuantitySheets
                        .Where(qs => qs.LotNo == lotNo)
                        .ToList();

                    var completedSheets = lotQuantitySheets.Count(qs =>
                        transactions.Any(t =>
                            t.QuantitysheetId == qs.QuantitySheetId &&
                            t.ProcessId == process.ProcessId &&
                            t.Status == 2
                        )
                    );

                    var totalSheets = lotQuantitySheets.Count;
                    var lotQuantity = lotQuantitySheets.Sum(qs => qs.Quantity); // Sum quantity for the lot

                    var percentage = totalSheets > 0
                        ? Math.Round((double)completedSheets / totalSheets * 100, 2)
                        : 0;

                    totalProcessSheets += totalSheets;
                    totalProcessCompletedSheets += completedSheets;

                    lotsList.Add(new
                    {
                        lotNumber = lotNo,
                        percentage = percentage,
                        totalSheets = totalSheets,
                        completedSheets = completedSheets,
                        lotQuantity = lotQuantity // Add quantity for the lot
                    });
                }

                totalProjectSheets += totalProcessSheets;
                totalProjectCompletedSheets += totalProcessCompletedSheets;
                totalProjectQuantity += totalProcessQuantity;

                var overallPercentage = totalProcessSheets > 0
                    ? Math.Round((double)totalProcessCompletedSheets / totalProcessSheets * 100, 2)
                    : 0;

                processesList.Add(new
                {
                    processId = process.ProcessId,
                    statistics = new
                    {
                        totalLots = lotsList.Count,
                        totalSheets = totalProcessSheets,
                        completedSheets = totalProcessCompletedSheets,
                        totalQuantity = totalProcessQuantity, // Add total quantity for the process
                        overallPercentage = overallPercentage
                    },
                    lots = lotsList
                });
            }

            var overallProjectPercentage = totalProjectSheets > 0
                ? Math.Round((double)totalProjectCompletedSheets / totalProjectSheets * 100, 2)
                : 0;

            var result = new
            {
                totalProcesses = processes.Count,
                overallProjectQuantity = totalProjectQuantity, // Add overall project quantity
                overallProjectPercentage = overallProjectPercentage,
                processes = processesList
            };

            return Ok(result);
        }


        [HttpGet("exists/{projectId}")]
        public async Task<ActionResult<bool>> TransactionExistsByProject(int projectId)
        {
            try
            {
                bool exists = await _context.Transaction
                    .AnyAsync(t => t.ProjectId == projectId);

                return Ok(exists);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }


        [HttpGet("CheckTransaction")]
        public async Task<IActionResult> CheckTransaction(int projectId, int lotNo)
        {
            // Step 1: Get all QuantitySheetIds from the Transaction table where the combination of ProjectId and LotNo matches
            var quantitySheetIds = await _context.Transaction
                .Where(t => t.ProjectId == projectId && t.LotNo == lotNo)
                .Select(t => t.QuantitysheetId)
                .ToListAsync();

            

            // Return the list of CatchNos in JSON format
            return Ok(quantitySheetIds);
        }
        [HttpGet("{projectId}/withlogs")]
        public async Task<IActionResult> GetTransactionsWithEventLogsByProjectId(int projectId)
        {
            try
            {
                var result = await (from e in _context.EventLogs
                                    join t in _context.Transaction on e.TransactionId equals t.TransactionId
                                    join q in _context.QuantitySheets on t.QuantitysheetId equals q.QuantitySheetId
                                    where t.ProjectId == projectId
                                    select new
                                    {
                                        EventLog = new
                                        {
                                            e.EventID,
                                            Event = e.Event ?? string.Empty,
                                            Category = e.Category ?? string.Empty,
                                            EventTriggeredBy = e.EventTriggeredBy,
                                            e.LoggedAT,
                                            OldValue = e.OldValue ?? string.Empty,
                                            NewValue = e.NewValue ?? string.Empty,
                                            e.TransactionId
                                        },
                                        Transaction = new
                                        {
                                            t.TransactionId,
                                            InterimQuantity = t.InterimQuantity,
                                            Remarks = t.Remarks ?? string.Empty,
                                            VoiceRecording = t.VoiceRecording ?? string.Empty,
                                            t.ProjectId,
                                            t.QuantitysheetId,
                                            t.ProcessId,
                                            t.ZoneId,
                                            t.MachineId,
                                            Status = t.Status.ToString(),
                                            AlarmId = t.AlarmId ?? string.Empty,
                                            LotNo = t.LotNo,
                                            TeamId = t.TeamId
                                        },
                                        QuantitySheet = new
                                        {
                                            q.QuantitySheetId,
                                            q.CatchNo,
                                            Paper = q.Paper ?? string.Empty,
                                            q.ExamDate,
                                            q.ExamTime,
                                            q.Course,
                                            q.Subject,
                                            InnerEnvelope = q.InnerEnvelope ?? string.Empty,
                                            OuterEnvelope = q.OuterEnvelope ?? 0,
                                            LotNo = q.LotNo ?? string.Empty,
                                            q.Quantity,
                                            Pages = q.Pages ?? 0,
                                            q.PercentageCatch,
                                            q.ProjectId,
                                            Status = q.Status ?? 0,
                                            ProcessId = q.ProcessId // This is a List<int>, handle as needed
                                        }
                                    }).ToListAsync();

                if (!result.Any())
                {
                    return NotFound($"No data found for ProjectId: {projectId}");
                }

                return Ok(result);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred: {ex.Message}");
                return StatusCode(500, "An error occurred while processing your request.");
            }
        }

        [HttpGet("Process-Train")]
        public async Task<ActionResult> GetProcessCalculation(int projectId, int LotNo)
        {
            // Step 1: Get the process sequence for the given projectId from the ProjectProcess table
            var processSequence = await _context.ProjectProcesses
                .Where(pp => pp.ProjectId == projectId)
                .OrderBy(pp => pp.Sequence) // Assuming 'Sequence' field determines the order
                .Select(pp => new { pp.ProcessId, pp.Sequence })
                .ToListAsync();
            foreach (var p in processSequence)
            {
                Console.WriteLine(p); //15,1,2,3,4,
            }


            var cuttingsequence = processSequence.Where(p => p.ProcessId == 4).FirstOrDefault();


            var process1 = processSequence.FirstOrDefault();
            var project = await _context.Projects
                .Where(p => p.ProjectId == projectId)
                .Select(p => new { p.TypeId, p.NoOfSeries })
                .FirstOrDefaultAsync();

            var type = project.TypeId;
            Console.WriteLine(type); //1
            var noofseries = project.NoOfSeries;
            Console.WriteLine(noofseries); //4



            var transactionDetails = await (from t in _context.Transaction
                                            join q in _context.QuantitySheets on t.QuantitysheetId equals q.QuantitySheetId
                                            where t.ProjectId == projectId && t.LotNo == LotNo && t.ProcessId == process1.ProcessId && t.Status == 2
                                            select new
                                            {
                                                t.TransactionId,
                                                t.ProjectId,
                                                t.QuantitysheetId,
                                                q.ProcessId,
                                                t.ZoneId,
                                                t.MachineId,
                                                t.Status,
                                                t.AlarmId,
                                                t.LotNo,
                                                t.TeamId,
                                                q.Quantity,
                                            }).ToListAsync();

            var transactionsinCTP = transactionDetails.Where(t => t.ProcessId.Contains(1)).ToList();
            var sumOfQuantitiesInCTP = transactionsinCTP.Sum(t => t.Quantity);
            var transactionsinDigital = transactionDetails.Where(t => t.ProcessId.Contains(3)).ToList();
            var sumOfQuantitiesInDigital = transactionsinDigital.Sum(t => t.Quantity);
            var CountofCatchesInCTP = transactionsinCTP.Count();
            var CountofCatchesInDigital = transactionsinDigital.Count();
            foreach (var tctp in transactionsinDigital)
            {
                string processIds = string.Join(",", tctp.ProcessId);

                Console.WriteLine(tctp.QuantitysheetId + ">" + tctp.Quantity + ">" + processIds);
            }

            // Step 2: Get the first transaction's ProcessId for the given ProjectId and LotNo
            var sequence1ProcessId = await _context.ProjectProcesses
                .Where(pp => pp.ProjectId == projectId && pp.Sequence == 1)
                .Select(pp => pp.ProcessId)
                .FirstOrDefaultAsync();

            // Step 3: Get all processes related to the project
            var allProcesses = await _context.ProjectProcesses
                .Where(pp => pp.ProjectId == projectId)
                .Join(_context.Processes,
                      pp => pp.ProcessId,
                      p => p.Id,
                      (pp, p) => new { pp.ProcessId, p.Name, p.ProcessType, pp.Sequence, p.RangeStart })
                .ToListAsync();

            // Step 4: Retrieve transactions and perform a LEFT JOIN with allProcesses
            var transactions = await _context.Transaction
                .Where(t => t.ProjectId == projectId && t.LotNo == LotNo)
                .Join(_context.QuantitySheets,
                      t => t.QuantitysheetId,
                      q => q.QuantitySheetId,
                      (t, q) => new { t.ProcessId, t.Status, q.Quantity, q.CatchNo })
                .ToListAsync();

            // Step 5: Combine allProcesses with transactions using a LEFT JOIN in-memory
            var processCounts = allProcesses
                .GroupJoin(transactions,
                    process => process.ProcessId,
                    transaction => transaction.ProcessId,
                    (process, transGroup) => new ProcessCalculationResult
                    {
                        ProcessId = process.ProcessId,
                        ProcessName = process.Name,
                        ProcessType = process.ProcessType,
                        WIPCount = transGroup.Count(t => t.Status == 1), // Count for Status 1
                        CompletedCount = transGroup.Count(t => t.Status == 2), // Count for Status 2
                        WIPTotalQuantity = transGroup.Where(t => t.Status == 1).Sum(t => t.Quantity), // Total Quantity for Status 1
                        CompletedTotalQuantity = transGroup.Where(t => t.Status == 2).Sum(t => t.Quantity), // Total Quantity for Status 2
                        InitialTotalQuantity = transGroup.Sum(t => t.Quantity), // Total Quantity across all statuses
                        RemainingQuantity = transGroup.Sum(t => t.Quantity) - (transGroup.Where(t => t.Status == 1).Sum(t => t.Quantity) + transGroup.Where(t => t.Status == 2).Sum(t => t.Quantity)), // Remaining Quantity
                        TotalCatchNo = transGroup.Count(t => !string.IsNullOrEmpty(t.CatchNo)),  // Count non-null CatchNo values
                        RemainingCatchNo = transGroup.Count(t => !string.IsNullOrEmpty(t.CatchNo))
                            - transGroup.Where(t => t.Status == 1).Count(t => !string.IsNullOrEmpty(t.CatchNo))
                            - transGroup.Where(t => t.Status == 2).Count(t => !string.IsNullOrEmpty(t.CatchNo)), // Remaining CatchNo count
                    })
                .ToList();

            // Step 6: Ensure counts are 0 for processes with no transactions
            var finalizedProcessCounts = processCounts.Select(p => new ProcessCalculationResult
            {
                ProcessId = p.ProcessId,
                ProcessSequence = processSequence.FirstOrDefault(seq => seq.ProcessId == p.ProcessId)?.Sequence ?? 0,
                ProcessName = p.ProcessName,
                ProcessType = p.ProcessType,
                RangeStart = p.RangeStart,
                WIPCount = p.WIPCount > 0 ? p.WIPCount : 0,
                CompletedCount = p.CompletedCount > 0 ? p.CompletedCount : 0,
                WIPTotalQuantity = p.WIPTotalQuantity > 0 ? p.WIPTotalQuantity : 0,
                CompletedTotalQuantity = p.CompletedTotalQuantity > 0 ? p.CompletedTotalQuantity : 0,
                InitialTotalQuantity = p.InitialTotalQuantity > 0 ? p.InitialTotalQuantity : 0,
                RemainingQuantity = p.RemainingQuantity > 0 ? p.RemainingQuantity : 0,
                TotalCatchNo = p.TotalCatchNo > 0 ? p.TotalCatchNo : 0,
                RemainingCatchNo = p.RemainingCatchNo > 0 ? p.RemainingCatchNo : 0
            })
           .OrderBy(p => processSequence.FirstOrDefault(seq => seq.ProcessId == p.ProcessId)?.Sequence ?? int.MaxValue)
            .ToList();

            // Step 7: Adjust RemainingQuantity and TotalQuantity based on specific rules
            for (int i = 1; i < finalizedProcessCounts.Count; i++)
            {
                var currentProcess = finalizedProcessCounts[i];
                ProcessCalculationResult previousProcess = null;

                // If ProcessId is 4, set previous process to ProcessId 2
                if (currentProcess.ProcessId == 4)
                {
                    previousProcess = finalizedProcessCounts.FirstOrDefault(p => p.ProcessId == 2);
                }
                // If ProcessId is 3, set previous process to the one with sequence 1
                else if (currentProcess.ProcessId == 3)
                {
                    currentProcess.InitialTotalQuantity = sumOfQuantitiesInDigital;
                    currentProcess.TotalCatchNo = CountofCatchesInDigital;
                    currentProcess.RemainingQuantity = currentProcess.InitialTotalQuantity - currentProcess.WIPTotalQuantity - currentProcess.CompletedTotalQuantity;
                    currentProcess.RemainingCatchNo = currentProcess.TotalCatchNo - currentProcess.WIPCount - currentProcess.CompletedCount;
                }
                else if (currentProcess.ProcessId == 1)
                {
                    currentProcess.InitialTotalQuantity = sumOfQuantitiesInCTP;
                    currentProcess.TotalCatchNo = CountofCatchesInCTP;
                    currentProcess.RemainingQuantity = currentProcess.InitialTotalQuantity - currentProcess.WIPTotalQuantity - currentProcess.CompletedTotalQuantity;
                    currentProcess.RemainingCatchNo = currentProcess.TotalCatchNo - currentProcess.WIPCount - currentProcess.CompletedCount;
                }
                else if (currentProcess.ProcessSequence == cuttingsequence.Sequence + 1 && type == 1)
                {
                    previousProcess = finalizedProcessCounts.FirstOrDefault(p =>
                processSequence.FirstOrDefault(seq => seq.ProcessId == p.ProcessId)?.Sequence ==
                processSequence.FirstOrDefault(seq => seq.ProcessId == currentProcess.ProcessId)?.Sequence - 1);

                    var digitalprintingcompleted = (finalizedProcessCounts.FirstOrDefault(p => p.ProcessId == 3).CompletedTotalQuantity);
                    var digitalcatchCompleted = finalizedProcessCounts.FirstOrDefault(p => p.ProcessId == 3).CompletedCount;
                    currentProcess.InitialTotalQuantity = (digitalprintingcompleted / 4 + previousProcess.CompletedTotalQuantity / 4);
                    currentProcess.CompletedTotalQuantity /= (noofseries ?? 1);
                    currentProcess.WIPTotalQuantity /= (noofseries ?? 1);
                    currentProcess.CompletedCount /= (noofseries ?? 1);
                    currentProcess.WIPCount /= (noofseries ?? 1);
                    currentProcess.TotalCatchNo = (previousProcess.TotalCatchNo / 4 + digitalcatchCompleted / 4);
                    currentProcess.RemainingQuantity = currentProcess.InitialTotalQuantity - currentProcess.WIPTotalQuantity - currentProcess.CompletedTotalQuantity;
                    currentProcess.RemainingCatchNo = currentProcess.TotalCatchNo - currentProcess.WIPCount - currentProcess.CompletedCount;
                }

                else if (currentProcess.ProcessSequence == cuttingsequence.Sequence + 1)
                {
                    Console.WriteLine("Going in that" + (currentProcess.ProcessId));
                    previousProcess = finalizedProcessCounts.FirstOrDefault(p =>
               processSequence.FirstOrDefault(seq => seq.ProcessId == p.ProcessId)?.Sequence ==
               processSequence.FirstOrDefault(seq => seq.ProcessId == currentProcess.ProcessId)?.Sequence - 1);

                    var digitalprintingcompleted = finalizedProcessCounts.FirstOrDefault(p => p.ProcessId == 3).CompletedTotalQuantity;
                    var digitalcatchCompleted = finalizedProcessCounts.FirstOrDefault(p => p.ProcessId == 3).CompletedCount;
                    currentProcess.InitialTotalQuantity = digitalprintingcompleted + previousProcess.CompletedTotalQuantity;
                    currentProcess.TotalCatchNo = previousProcess.TotalCatchNo + digitalcatchCompleted;
                    currentProcess.RemainingQuantity = currentProcess.InitialTotalQuantity - currentProcess.WIPTotalQuantity - currentProcess.CompletedTotalQuantity;
                    currentProcess.RemainingCatchNo = currentProcess.TotalCatchNo - currentProcess.WIPCount - currentProcess.CompletedCount;
                }
                else if (currentProcess.ProcessType == "Independent")
                {
                    // For other processes, set the previous process based on sequence
                    previousProcess = finalizedProcessCounts.FirstOrDefault(p =>
                   processSequence.FirstOrDefault(seq => seq.ProcessId == p.ProcessId)?.Sequence == currentProcess.RangeStart);

                }

                else
                {
                    // For other processes, set the previous process based on sequence
                    previousProcess = finalizedProcessCounts.FirstOrDefault(p =>
                processSequence.FirstOrDefault(seq => seq.ProcessId == p.ProcessId)?.Sequence ==
                processSequence.FirstOrDefault(seq => seq.ProcessId == currentProcess.ProcessId)?.Sequence - 1);

                }

                // Only adjust if previous process is found
                if (previousProcess != null && !(currentProcess.ProcessSequence == cuttingsequence.Sequence + 1) && type != 1)
                {

                    currentProcess.InitialTotalQuantity = previousProcess.CompletedTotalQuantity;
                    currentProcess.RemainingQuantity = currentProcess.InitialTotalQuantity - currentProcess.WIPTotalQuantity - currentProcess.CompletedTotalQuantity;
                    currentProcess.RemainingCatchNo = previousProcess.CompletedCount - currentProcess.WIPCount - currentProcess.CompletedCount;
                    currentProcess.TotalCatchNo = previousProcess.CompletedCount;
                }
                else if ((type == 1 && previousProcess != null) && !(currentProcess.ProcessSequence == cuttingsequence.Sequence + 1 && type == 1) && currentProcess.ProcessSequence > cuttingsequence.Sequence + 1)
                {
                    Console.WriteLine("Current Process" + (currentProcess.ProcessId));
                    Console.WriteLine("Previous Process" + (previousProcess.ProcessId));
                    currentProcess.InitialTotalQuantity = previousProcess.CompletedTotalQuantity;
                    currentProcess.CompletedTotalQuantity /= (noofseries ?? 1);
                    currentProcess.WIPTotalQuantity /= (noofseries ?? 1);
                    currentProcess.CompletedCount /= (noofseries ?? 1);
                    currentProcess.WIPCount /= (noofseries ?? 1);
                    // Console.WriteLine("Current Process" + (currentProcess.CompletedTotalQuantity));
                    currentProcess.RemainingQuantity = currentProcess.InitialTotalQuantity - currentProcess.WIPTotalQuantity - currentProcess.CompletedTotalQuantity;
                    currentProcess.RemainingCatchNo = previousProcess.CompletedCount - currentProcess.WIPCount - currentProcess.CompletedCount;
                    currentProcess.TotalCatchNo = previousProcess.CompletedCount;
                    //Console.WriteLine("Current Process" + (currentProcess.TotalCatchNo));

                }
                else if ((type == 1 && previousProcess != null) && !(currentProcess.ProcessSequence == cuttingsequence.Sequence + 1 && type == 1) && currentProcess.ProcessSequence < cuttingsequence.Sequence + 1)
                {
                    currentProcess.InitialTotalQuantity = previousProcess.CompletedTotalQuantity;
                    currentProcess.RemainingQuantity = currentProcess.InitialTotalQuantity - currentProcess.WIPTotalQuantity - currentProcess.CompletedTotalQuantity;
                    currentProcess.RemainingCatchNo = previousProcess.CompletedCount - currentProcess.WIPCount - currentProcess.CompletedCount;
                    currentProcess.TotalCatchNo = previousProcess.CompletedCount;
                }
                else
                {
                }
            }

            return Ok(finalizedProcessCounts);
        }

        [HttpGet("StatusDetails")]
        public async Task<ActionResult> GetDetailsforProcess(int projectId, string LotNo, int ProcessIDFilter, int StatustoFind)
        {
            var qsitems = await _context.QuantitySheets
                .Where(qs => qs.ProjectId == projectId && qs.LotNo == LotNo)
                .ToListAsync();
            // Filter in memory for matching ProcessId
            qsitems = qsitems
                .Where(qs => qs.ProcessId.Contains(ProcessIDFilter))
                .ToList();
            var transactions = await _context.Transaction
                .Where(t => t.ProjectId == projectId && t.LotNo == Int32.Parse(LotNo) && t.ProcessId == ProcessIDFilter)
                .ToListAsync(); 
            // Left join QuantitySheets with Transactions
            var joinedResults = from qs in qsitems
                                join t in transactions
                                on qs.QuantitySheetId equals t.QuantitysheetId into gj
                                from subT in gj.DefaultIfEmpty() // Left join
                                where (StatustoFind == 0
                                       ? subT == null || subT.Status == 0  // No entry in Transaction OR Status == 0
                                       : subT != null && subT.Status == StatustoFind) // For other statuses
                                select new
                                {
                                    QuantitySheetId = qs.QuantitySheetId,
                                    CatchNo = qs.CatchNo,
                                    Paper = qs.Paper,
                                    ExamDate = qs.ExamDate,
                                    ExamTime = qs.ExamTime,
                                    Course = qs.Course,
                                    Subject = qs.Subject,
                                    Quantity = qs.Quantity,
                                    Status = subT?.Status ?? 0 // If null (no transaction), assume Status = 0
                                };
            var resultList = joinedResults.ToList();
            return Ok(resultList);
        }






        public class ProcessCalculationResult
        {
            public int ProcessId { get; set; }
            public int ProcessSequence { get; set; }
            public string ProcessName { get; set; }
            public string ProcessType { get; set; }
            public int RangeStart { get; set; }
            public int WIPCount { get; set; }
            public int CompletedCount { get; set; }
            public double WIPTotalQuantity { get; set; }
            public double CompletedTotalQuantity { get; set; }
            public double InitialTotalQuantity { get; set; }
            public double RemainingQuantity { get; set; }
            public int TotalCatchNo { get; set; }
            public int RemainingCatchNo { get; set; }
        }

    }
}