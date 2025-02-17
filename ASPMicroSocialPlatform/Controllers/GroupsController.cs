﻿using ASPMicroSocialPlatform.Data;
using ASPMicroSocialPlatform.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using System.Text.RegularExpressions;
using System.Text.Json;


namespace ASPMicroSocialPlatform.Controllers
{
    public class GroupsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly IWebHostEnvironment _env;

        public GroupsController(ApplicationDbContext context, UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager, IWebHostEnvironment env)
        {
            _context = context;
            _userManager = userManager;
            _roleManager = roleManager;
            _env = env;
        }

        [HttpGet]
        [Authorize]
        public async Task<IActionResult> Search(string? search)
        {
            if (string.IsNullOrEmpty(search))
                search = "";


            var users = await _context.Users
                .Where(u => u.FirstName.Contains(search) ||
                            u.LastName.Contains(search) ||
                            u.Email.Contains(search))
                .ToListAsync();

            return PartialView("_UserSearchResultsPartial", users);
        }



        [Authorize]
        public async Task<IActionResult> Index()
        {
            var userId = _userManager.GetUserId(User);
            ViewBag.CurrentUserId = userId;

            if (userId == null)
            {
                return RedirectToAction("Index", "Home");
            }

            var groups = await _context.UserGroups
                .Include(ug => ug.Group)
                    .ThenInclude(g => g.UserGroups)
                        .ThenInclude(ug => ug.User)
                .Where(ug => ug.UserId == userId)
                .Select(ug => ug.Group)
                .ToListAsync();

            if (User.IsInRole("Admin"))
            {
                groups = await _context.Groups
                    .Include(g => g.UserGroups)
                        .ThenInclude(ug => ug.User)
                    .ToListAsync();
            }


            return View(groups);
        }


        [HttpGet]
        [Authorize]
        public IActionResult New()
        {
            return View(new GroupCreateModel());
        }


        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> New(GroupCreateModel model)
        {
            // print the data in the model
            var json = JsonSerializer.Serialize(model);
            Console.WriteLine(json);

            // Fetch all selected users to ensure model is complete
            var selectedUsers = await _context.Users
                .Where(u => model.SelectedUserIds.Contains(u.Id))
                .ToListAsync();
            model.SelectedUsers = selectedUsers;

            Console.WriteLine("Gonna check the model validity");

            if (!ModelState.IsValid) return View(model);

            Console.WriteLine("Model is valid!!!!!!!!!!!!!!!!!");

            var group = new ASPMicroSocialPlatform.Models.Group
            {
                Name = model.Name,
                Description = model.Description
            };

            _context.Groups.Add(group);
            await _context.SaveChangesAsync();

            var currentUserId = _userManager.GetUserId(User);

            // Add creator as moderator
            _context.UserGroups.Add(new UserGroup
            {
                GroupId = group.Id,
                UserId = currentUserId,
                IsModerator = true
            });

            // Add all selected users
            foreach (var userId in model.SelectedUserIds.Distinct())
            {
                if (userId != currentUserId)  // Don't add creator twice
                {
                    _context.UserGroups.Add(new UserGroup
                    {
                        GroupId = group.Id,
                        UserId = userId,
                        IsModerator = false
                    });
                }
            }

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }


        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddUser([FromForm] string userId, [FromForm] List<string> existingUserIds)
        {
            var model = new GroupCreateModel
            {
                SelectedUserIds = existingUserIds ?? new List<string>()
            };


            var user = await _context.Users.FindAsync(userId);
            if (user != null && !model.SelectedUserIds.Contains(userId))
            {
                model.SelectedUserIds.Add(userId);
            }
            model.SelectedUsers = await _context.Users
                .Where(u => model.SelectedUserIds.Contains(u.Id))
                .ToListAsync();

            return PartialView("_SelectedUsersPartial", model);
        }

        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveUser([FromForm] string userId, [FromForm] List<string> existingUserIds)
        {
            var model = new GroupCreateModel
            {
                SelectedUserIds = existingUserIds ?? new List<string>()
            };

            model.SelectedUserIds.Remove(userId);
            model.SelectedUsers = await _context.Users
                .Where(u => model.SelectedUserIds.Contains(u.Id))
                .ToListAsync();

            return PartialView("_SelectedUsersPartial", model);
        }



        [Authorize]
        public async Task<IActionResult> Edit(int id)
        {
            var currentUserId = _userManager.GetUserId(User);
            var group = await _context.Groups
                .Include(g => g.UserGroups)
                    .ThenInclude(ug => ug.User)
                .FirstOrDefaultAsync(g => g.Id == id);

            if (group == null)
                return NotFound();

            var userGroup = group.UserGroups.FirstOrDefault(ug => ug.UserId == currentUserId);
            if (userGroup?.IsModerator != true && !User.IsInRole("Admin"))
                return Forbid();

            var model = new GroupCreateModel
            {
                GroupId = group.Id, // id al grupui
                Name = group.Name,
                Description = group.Description,
                SelectedUserIds = group.UserGroups.Select(ug => ug.UserId).ToList(),
                SelectedUsers = group.UserGroups.Select(ug => ug.User).ToList()
            };

            return View(model);
        }

        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, GroupCreateModel model)
        {
            // json for the model
            Console.WriteLine("Model: " + JsonSerializer.Serialize(model));
            var selectedUsers = await _context.Users
                .Where(u => model.SelectedUserIds.Contains(u.Id))
                .ToListAsync();
            model.SelectedUsers = selectedUsers;

            if (!ModelState.IsValid)
                return View(model);

            var group = await _context.Groups
                .Include(g => g.UserGroups)
                .FirstOrDefaultAsync(g => g.Id == id);

            if (group == null)
                return NotFound();

            var currentUserId = _userManager.GetUserId(User);
            var userGroup = group.UserGroups.FirstOrDefault(ug => ug.UserId == currentUserId);

            if (userGroup?.IsModerator != true && !User.IsInRole("Admin"))
                return Forbid();

            group.Name = model.Name;
            group.Description = model.Description;

            // Handle member changes
            var existingUserIds = group.UserGroups.Select(ug => ug.UserId).ToList();
            var usersToAdd = model.SelectedUserIds.Except(existingUserIds);
            var usersToRemove = existingUserIds.Except(model.SelectedUserIds);

            foreach (var userId in usersToAdd)
            {
                _context.UserGroups.Add(new UserGroup
                {
                    GroupId = group.Id,
                    UserId = userId,
                    IsModerator = false
                });
            }

            _context.UserGroups.RemoveRange(
                group.UserGroups.Where(ug => usersToRemove.Contains(ug.UserId)));

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        [Authorize]
        public async Task<IActionResult> Show(int id)
        {
            var currentUserId = _userManager.GetUserId(User);
            var group = await _context.Groups
                .Include(g => g.UserGroups)
                .Include(m => m.Messages)
                    .ThenInclude(ug => ug.User)
                .FirstOrDefaultAsync(g => g.Id == id);

            if (group == null)
                return NotFound();

            var userGroup = group.UserGroups.FirstOrDefault(ug => ug.UserId == currentUserId);
            if (userGroup == null && !User.IsInRole("Admin"))
                return Forbid();

            if(userGroup != null)
                ViewBag.IsModerator = userGroup.IsModerator;
            else
                ViewBag.IsModerator = false;
            ViewBag.CurrentUserId = currentUserId;
            

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return PartialView("Show", group);
            }

            return View("Index", await GetUserGroups(currentUserId));
        }


        private async Task<List<ASPMicroSocialPlatform.Models.Group>> GetUserGroups(string userId)
        {
            /*return await _context.UserGroups
                .Include(ug => ug.Group)
                    .ThenInclude(g => g.UserGroups)
                        .ThenInclude(ug => ug.User)
                .Where(ug => ug.UserId == userId)
                .Select(ug => ug.Group)
                .ToListAsync();*/
            // if the user is an admin, return all groups
            if (User.IsInRole("Admin"))
            {
                return await _context.Groups
                    .Include(g => g.UserGroups)
                        .ThenInclude(ug => ug.User)
                    .ToListAsync();
            }

            return await _context.UserGroups
                .Include(ug => ug.Group)
                    .ThenInclude(g => g.UserGroups)
                        .ThenInclude(ug => ug.User)
                .Where(ug => ug.UserId == userId)
                .Select(ug => ug.Group)
                .ToListAsync();
        }

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> Delete(int id)
        {
            var group = await _context.Groups
                .Include(g => g.UserGroups)
                .FirstOrDefaultAsync(g => g.Id == id);

            if (group == null)
                return NotFound();

            var currentUserId = _userManager.GetUserId(User);
            var userGroup = group.UserGroups.FirstOrDefault(ug => ug.UserId == currentUserId);

            if (userGroup?.IsModerator != true && !User.IsInRole("Admin"))
                return Forbid();

            _context.Groups.Remove(group);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        [Authorize]
        public async Task<IActionResult> Leave(int id)
        {

            var userId = _userManager.GetUserId(User);

            // look through UserGroups and find the one with the current user and the group
            var userGroup = await _context.UserGroups
                .FirstOrDefaultAsync(ug => ug.UserId == userId && ug.GroupId == id);

            if (userGroup == null)
            {
                TempData["message"] = "Nu faci parte din acest grup!";
                TempData["messageType"] = "error";
                return RedirectToAction(nameof(Index));
            }

            // remove the user from the group and if he is the last one, delete the group
            _context.UserGroups.Remove(userGroup);
            await _context.SaveChangesAsync();

            var group = await _context.Groups
                .Include(g => g.UserGroups)
                .FirstOrDefaultAsync(g => g.Id == id);

            if (group != null) {
                if (group.UserGroups.Count == 0)
                {
                    _context.Groups.Remove(group);
                    await _context.SaveChangesAsync();
                }
            }



            TempData["message"] = "Ai parasit grupul cu succes!";
            TempData["messageType"] = "success";

            return RedirectToAction(nameof(Index));
        }
    }

        




    
}
