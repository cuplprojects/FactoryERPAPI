/*using ERPAPI.Data;
using ERPAPI.Model;
using Microsoft.EntityFrameworkCore;

namespace ERPAPI.Services
{
    public class ProjectService : IProjectService
    {
        private readonly AppDbContext _appDbContext;

        public ProjectService(AppDbContext appDbContext)
        {
            _appDbContext = appDbContext;
        }

        public async Task<IEnumerable<Project>> GetAllProjects()
        {
            return await _appDbContext.Projects.ToListAsync();
        }
    }
}
*/


using ERPAPI.Data;
using ERPAPI.Model;
using Microsoft.EntityFrameworkCore;


namespace ProjectService.Services
{
    public class ProjectService
    {
        private readonly AppDbContext _context;

        public ProjectService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<List<Project>> GetDistinctProjectsForUser(int userId)
        {
            var user = await _context.Users.FindAsync(userId);
            if (user == null)
                return new List<Project>();

            var ongoingProjects = await _context.QuantitySheets
                .Where(q => q.Status == 1)
                .GroupBy(p => p.ProjectId)
                .Select(group => group.Key)
                .ToListAsync();

            if (user.RoleId < 5)
            {
                return await _context.Projects
                    .Where(p => p.Status == true)
                    .OrderByDescending(p => p.ProjectId)
                    .ToListAsync();
            }
            else
            {
                var projectProcesses = await _context.ProjectProcesses.AsNoTracking().ToListAsync();

                var userAssignedProcesses = projectProcesses
                    .Where(pp => pp.UserId.Contains(userId))
                    .Select(pp => pp.ProjectId)
                    .Distinct()
                    .ToList();

                var userAssignedOngoingProjects = userAssignedProcesses
                    .Where(up => ongoingProjects.Contains(up))
                    .ToList();

                if (!userAssignedOngoingProjects.Any())
                    return new List<Project>();

                return await _context.Projects
                    .Where(p => userAssignedOngoingProjects.Contains(p.ProjectId))
                    .OrderByDescending(p => p.ProjectId)
                    .ToListAsync();
            }
        }
    }
}
