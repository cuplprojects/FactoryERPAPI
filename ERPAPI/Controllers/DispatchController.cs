using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ERPAPI.Data;
using ERPAPI.Model;
using ERPAPI.Services;
using ERPAPI.Service;

namespace ERPAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class DispatchController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly ILoggerService _loggerService;

        public DispatchController(AppDbContext context, ILoggerService loggerService)
        {
            _context = context;
            _loggerService = loggerService;
        }

        // GET: api/Dispatch
        [HttpGet]
        public async Task<ActionResult<IEnumerable<object>>> GetDispatches()
        {
            try
            {
                var dispatches = await _context.Dispatch.ToListAsync();
                var dispatchesWithDetails = dispatches.Select(dispatch => new
                {
                    dispatch.Id,
                    dispatch.ProcessId,
                    dispatch.ProjectId,
                    dispatch.LotNo,
                    dispatch.BoxCount,
                    dispatch.MessengerName,
                    dispatch.MessengerMobile,
                    dispatch.DispatchMode,
                    dispatch.VehicleNumber,
                    dispatch.DriverName,
                    dispatch.DriverMobile,
                    dispatch.CreatedAt,
                    dispatch.UpdatedAt,
                    dispatch.Status,
                    dispatch.DispatchDate
                }).ToList();

              
                return Ok(dispatchesWithDetails);
            }
            catch (Exception ex)
            {
       
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpGet("dispatch-summary-today")]
        public async Task<ActionResult<object>> GetTodayDispatchWithQuantitySummary()
        {
            try
            {
                var today = DateTime.Today;

                // 1. Get all dispatches with today's DispatchDate
                var dispatches = await _context.Dispatch
                    .Where(d => d.DispatchDate >= today && d.DispatchDate < today.AddDays(1))
                    .ToListAsync();

                if (dispatches == null || !dispatches.Any())
                {
                    return NotFound("No dispatches found for today.");
                }


                var dispatchLotNos = dispatches.Select(d => d.LotNo).Distinct().ToList();
                var projectIds = dispatches.Select(d => d.ProjectId).Distinct().ToList();

                // 2. Get QuantitySheet records matching those LotNos and ProjectIds
                var quantitySheets = await _context.QuantitySheets
                    .Where(q => dispatchLotNos.Contains(q.LotNo) && projectIds.Contains(q.ProjectId))
                    .ToListAsync();

                // 3. Calculate totals
                var totalCatches = quantitySheets.Select(q => q.CatchNo).Count();
                var totalQuantity = quantitySheets.Sum(q => q.Quantity);

                // 4. Min/Max exam dates
                DateTime? examFrom = null;
                DateTime? examTo = null;
                var validExamDates = quantitySheets
                    .Where(q => DateTime.TryParse(q.ExamDate, out _))
                    .Select(q => DateTime.Parse(q.ExamDate))
                    .ToList();

                if (validExamDates.Any())
                {
                    examFrom = validExamDates.Min();
                    examTo = validExamDates.Max();
                }

                // 5. Build response
                var response = new
                {
                    Dispatches = dispatches.Select(dispatch =>
                    {
                        var qsForDispatch = quantitySheets
                            .Where(q => q.ProjectId == dispatch.ProjectId && q.LotNo == dispatch.LotNo)
                            .ToList();

                        var totalCatches = qsForDispatch.Select(q => q.CatchNo).Count();
                        var totalQuantity = qsForDispatch.Sum(q => q.Quantity);

                        DateTime? examFrom = null;
                        DateTime? examTo = null;

                        var validExamDates = qsForDispatch
                            .Where(q => DateTime.TryParse(q.ExamDate, out _))
                            .Select(q => DateTime.Parse(q.ExamDate))
                            .ToList();

                        if (validExamDates.Any())
                        {
                            examFrom = validExamDates.Min();
                            examTo = validExamDates.Max();
                        }

                        return new
                        {
                            dispatch.Id,
                            dispatch.ProjectId,
                            dispatch.LotNo,
                            dispatch.BoxCount,
                            dispatch.DispatchDate,
                            QuantitySheetSummary = new
                            {
                                TotalCatches = totalCatches,
                                TotalQuantity = totalQuantity,
                                ExamFrom = examFrom,
                                ExamTo = examTo
                            }
                        };
                    })
                };

                return Ok(response);
            }
            catch (Exception ex)
            {
                _loggerService.LogError("Error in GetTodayDispatchWithQuantitySummary", ex.Message, nameof(DispatchController));
                return StatusCode(500, "Internal server error");
            }
        }



        // GET: api/Dispatch/5
        [HttpGet("{id}")]
        public async Task<ActionResult<Dispatch>> GetDispatch(int id)
        {
            try
            {
                var dispatch = await _context.Dispatch.FindAsync(id);

                if (dispatch == null)
                {
                   
                    return NotFound();
                }

              
                return Ok(dispatch);
            }
            catch (Exception)
            {
              
                return StatusCode(500, "Internal server error");
            }
        }

        // GET: api/Dispatch/project/{projectId}/lot/{lotNo}
        [HttpGet("project/{projectId}/lot/{lotNo}")]
        public async Task<ActionResult<IEnumerable<object>>> GetDispatchByProjectAndLot(int projectId, string lotNo)
        {
            try
            {
                // Fetch the dispatch records based on projectId and lotNo
                var dispatches = await _context.Dispatch
                    .Where(d => d.ProjectId == projectId && d.LotNo == lotNo)
                    .ToListAsync();

                // If no dispatch records found, return NotFound
                if (dispatches == null || !dispatches.Any())
                {
                    
                    return NotFound();
                }

                // Select relevant details to return
                var dispatchesWithDetails = dispatches.Select(dispatch => new
                {
                    dispatch.Id,
                    dispatch.ProcessId,
                    dispatch.ProjectId,
                    dispatch.LotNo,
                    dispatch.BoxCount,
                    dispatch.MessengerName,
                    dispatch.MessengerMobile,
                    dispatch.DispatchMode,
                    dispatch.VehicleNumber,
                    dispatch.DriverName,
                    dispatch.DriverMobile,
                    dispatch.CreatedAt,
                    dispatch.UpdatedAt,
                    dispatch.Status,
                    dispatch.DispatchDate
                }).ToList();

              
                return Ok(dispatchesWithDetails);
            }
            catch (Exception)
            {
            
                return StatusCode(500, "Internal server error");
            }
        }

        // GET: api/Dispatch/project/{projectId}/lot/{lotNo}
        [HttpGet("project/{projectId}")]
        public async Task<ActionResult<IEnumerable<object>>> GetDispatchByProject(int projectId)
        {
            try
            {
                // Fetch the dispatch records based on projectId and lotNo
                var dispatches = await _context.Dispatch
                    .Where(d => d.ProjectId == projectId)
                    .ToListAsync();

                // If no dispatch records found, return NotFound
                if (dispatches == null || !dispatches.Any())
                {

                    return NotFound();
                }

                // Select relevant details to return
                var dispatchesWithDetails = dispatches.Select(dispatch => new
                {
                    dispatch.Id,
                    dispatch.ProcessId,
                    dispatch.ProjectId,
                    dispatch.LotNo,
                    dispatch.BoxCount,
                    dispatch.MessengerName,
                    dispatch.MessengerMobile,
                    dispatch.DispatchMode,
                    dispatch.VehicleNumber,
                    dispatch.DriverName,
                    dispatch.DriverMobile,
                    dispatch.CreatedAt,
                    dispatch.UpdatedAt,
                    dispatch.Status,
                    dispatch.DispatchDate
                }).ToList();


                return Ok(dispatchesWithDetails);
            }
            catch (Exception)
            {

                return StatusCode(500, "Internal server error");
            }
        }

        // PUT: api/Dispatch/5
        [HttpPut("{id}")]
        public async Task<IActionResult> PutDispatch(int id, Dispatch dispatch)
        {
            if (id != dispatch.Id)
            {
                return BadRequest();
            }

            _context.Entry(dispatch).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
                _loggerService.LogEvent($"Updated dispatch with ID {id}", "Dispatch", User.Identity?.Name != null ? int.Parse(User.Identity.Name) : 0);
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!DispatchExists(id))
                {
                    _loggerService.LogEvent($"Dispatch with ID {id} not found during update", "Dispatch", User.Identity?.Name != null ? int.Parse(User.Identity.Name) : 0);
                    return NotFound();
                }
                else
                {
                    _loggerService.LogError("Concurrency error during dispatch update", "Dispatch", nameof(DispatchController));
                    throw;
                }
            }
            catch (Exception ex)
            {
                _loggerService.LogError("Error updating dispatch", ex.Message, nameof(DispatchController));
                return StatusCode(500, "Internal server error");
            }

            return NoContent();
        }

        // POST: api/Dispatch
        [HttpPost]
        public async Task<ActionResult<Dispatch>> PostDispatch(Dispatch dispatch)
        {
            try
            {
                var dispatched = await _context.Dispatch.Where(di => di.ProjectId == dispatch.ProjectId && di.LotNo == dispatch.LotNo).FirstOrDefaultAsync();
                if (dispatched != null)
                {
                  _context.Dispatch.Remove(dispatched);
                    await _context.SaveChangesAsync();
                }
                _context.Dispatch.Add(dispatch);
                await _context.SaveChangesAsync();

                _loggerService.LogEvent("Created a new dispatch", "Dispatch", User.Identity?.Name != null ? int.Parse(User.Identity.Name) : 0);
                return CreatedAtAction("GetDispatch", new { id = dispatch.Id }, dispatch);
            }
            catch (Exception ex)
            {
                _loggerService.LogError("Error creating dispatch", ex.Message, nameof(DispatchController));
                return StatusCode(500, "Internal server error");
            }
        }

        // DELETE: api/Dispatch/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteDispatch(int id)
        {
            try
            {
                var dispatch = await _context.Dispatch.FindAsync(id);
                if (dispatch == null)
                {
                    _loggerService.LogEvent($"Dispatch with ID {id} not found during delete", "Dispatch", User.Identity?.Name != null ? int.Parse(User.Identity.Name) : 0);
                    return NotFound();
                }

                _context.Dispatch.Remove(dispatch);
                await _context.SaveChangesAsync();

                _loggerService.LogEvent($"Deleted dispatch with ID {id}", "Dispatch", User.Identity?.Name != null ? int.Parse(User.Identity.Name) : 0);
                return NoContent();
            }
            catch (Exception ex)
            {
                _loggerService.LogError("Error deleting dispatch", ex.Message, nameof(DispatchController));
                return StatusCode(500, "Internal server error");
            }
        }

        private bool DispatchExists(int id)
        {
            return _context.Dispatch.Any(e => e.Id == id);
        }
    }
}
