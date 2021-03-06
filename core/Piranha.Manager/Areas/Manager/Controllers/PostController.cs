/*
 * Copyright (c) 2017-2018 Håkan Edling
 *
 * This software may be modified and distributed under the terms
 * of the MIT license.  See the LICENSE file for details.
 * 
 * https://github.com/piranhacms/piranha.core
 * 
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Piranha.Areas.Manager.Services;
using Piranha.Manager;
using Piranha.Services;

namespace Piranha.Areas.Manager.Controllers
{
    [Area("Manager")]
    public class PostController : ManagerAreaControllerBase
    {
        private const string COOKIE_SELECTEDSITE = "PiranhaManager_SelectedSite";
        private readonly PostEditService _editService;
        private readonly IContentService<Data.Post, Data.PostField, Piranha.Models.PostBase> _contentService;
        private readonly IHubContext<Hubs.PreviewHub> _hub;

        /// <summary>
        /// Default constructor.
        /// </summary>
        /// <param name="api">The current api</param>
        public PostController(IApi api, PostEditService editService, IContentServiceFactory factory, IHubContext<Hubs.PreviewHub> hub) : base(api)
        {
            _editService = editService;
            _contentService = factory.CreatePostService();
            _hub = hub;
        }

        /// <summary>
        /// Gets the edit view for a post.
        /// </summary>
        /// <param name="id">The post id</param>
        [Route("manager/post/{id:Guid}")]
        [Authorize(Policy = Permission.PostsEdit)]
        public IActionResult Edit(Guid id)
        {
            return View(_editService.GetById(id));
        }

        /// <summary>
        /// Adds a new page of the given type.
        /// </summary>
        /// <param name="type">The page type id</param>
        /// <param name="blogId">The blog id</param>
        [Route("manager/post/add/{type}/{blogId:Guid}")]
        [Authorize(Policy = Permission.PostsEdit)]
        public IActionResult Add(string type, Guid blogId)
        {
            var model = _editService.Create(type, blogId);

            return View("Edit", model);
        }

        [Route("manager/post/preview/{id:Guid}")]
        [Authorize(Policy = Permission.PagesEdit)]
        public IActionResult Preview(Guid id)
        {
            var post = _api.Posts.GetById<Piranha.Models.PostInfo>(id);

            if (post != null)
            {
                return View("_Preview", new Models.PreviewModel { Id = id, Permalink = post.Permalink });
            }
            return NotFound();
        }

        /// <summary>
        /// Saves the given post model
        /// </summary>
        /// <param name="model">The post model</param>
        [HttpPost]
        [Route("manager/post/save")]
        [Authorize(Policy = Permission.PostsSave)]
        public async Task<IActionResult> Save(Models.PostEditModel model)
        {
            // Validate
            if (string.IsNullOrWhiteSpace(model.Title))
            {
                return BadRequest();
            }
            if (string.IsNullOrWhiteSpace(model.SelectedCategory))
            {
                return BadRequest();
            }

            var ret = _editService.Save(model, out var alias);

            // Save
            if (ret)
            {
                await _hub?.Clients.All.SendAsync("Update", model.Id);

                if (!string.IsNullOrWhiteSpace(alias))
                {
                    return Json(new
                    {
                        Location = Url.Action("Edit", new { id = model.Id }),
                        AliasSuggestion = new
                        {
                            Alias = $"{model.BlogSlug}/{alias}",
                            Redirect = $"{model.BlogSlug}/{model.Slug}",
                            BlogId = model.BlogId,
                            PostId = model.Id
                        }
                    });
                }
                else
                {
                    return Json(new { Location = Url.Action("Edit", new { id = model.Id }) });
                }
            }
            else
            {
                return StatusCode(500);
            }
        }

        /// <summary>
        /// Saves and publishes the given post model.
        /// </summary>
        /// <param name="model">The post model</param>
        [HttpPost]
        [Route("manager/post/publish")]
        [Authorize(Policy = Permission.PostsPublish)]
        public IActionResult Publish(Models.PostEditModel model)
        {
            // Validate
            if (string.IsNullOrWhiteSpace(model.Title))
            {
                return BadRequest();
            }
            if (string.IsNullOrWhiteSpace(model.SelectedCategory))
            {
                return BadRequest();
            }

            // Save
            if (_editService.Save(model, out var alias, true))
            {
                return Json(new
                {
                    Published = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                    Location = Url.Action("Edit", new { id = model.Id })
                });
            }
            else
            {
                return StatusCode(500);
            }
        }

        /// <summary>
        /// Saves and unpublishes the given post model.
        /// </summary>
        /// <param name="model">The post model</param>
        [HttpPost]
        [Route("manager/post/unpublish")]
        [Authorize(Policy = Permission.PostsPublish)]
        public IActionResult UnPublish(Models.PostEditModel model)
        {
            if (_editService.Save(model, out var alias, false))
            {
                return Json(new
                {
                    Unpublished = true,
                    Location = Url.Action("Edit", new { id = model.Id })
                });
            }
            else
            {
                return StatusCode(500);
            }
        }

        /// <summary>
        /// Deletes the post with the given id.
        /// </summary>
        /// <param name="id">The unique id</param>
        [Route("manager/post/delete/{id:Guid}")]
        [Authorize(Policy = Permission.PostsDelete)]
        public IActionResult Delete(Guid id)
        {
            var post = _api.Posts.GetById(id);

            if (post != null)
            {
                _api.Posts.Delete(post);

                SuccessMessage("The post has been deleted");

                return RedirectToAction("Edit", "Page", new { id = post.BlogId });
            }
            else
            {
                ErrorMessage("The post could not be deleted");
                return RedirectToAction("List", "Page", new { id = "" });
            }
        }

        /// <summary>
        /// Adds a new region to a post.
        /// </summary>
        /// <param name="model">The model</param>
        [HttpPost]
        [Route("manager/post/region")]
        [Authorize(Policy = Permission.Posts)]
        public IActionResult AddRegion([FromBody]Models.PageRegionModel model)
        {
            var postType = _api.PostTypes.GetById(model.PageTypeId);

            if (postType != null)
            {
                var regionType = postType.Regions.SingleOrDefault(r => r.Id == model.RegionTypeId);

                if (regionType != null)
                {
                    var region = _contentService.CreateDynamicRegion(postType, model.RegionTypeId);

                    var editModel = (Models.PageEditRegionCollection)_editService.CreateRegion(regionType,
                        new List<object> { region });

                    ViewData.TemplateInfo.HtmlFieldPrefix = $"Regions[{model.RegionIndex}].FieldSets[{model.ItemIndex}]";
                    return View("EditorTemplates/PageEditRegionItem", editModel.FieldSets[0]);
                }
            }
            return new NotFoundResult();
        }


        [HttpPost]
        [Route("manager/post/alias")]
        [Authorize(Policy = Permission.PostsEdit)]
        public IActionResult AddAlias(Guid blogId, Guid postId, string alias, string redirect)
        {
            // Get the blog page
            var page = _api.Pages.GetById(blogId);

            if (page != null)
            {
                // Create alias
                Piranha.Manager.Utils.CreateAlias(_api, page.SiteId, alias, redirect);

                return Json("Ok");
            }
            return StatusCode(500);
        }

        /// <summary>
        /// Gets the post modal for the specified blog.
        /// </summary>
        /// <param name="siteId">The site id</param>
        /// <param name="blogId">The blog id</param>
        [Route("manager/post/modal/{siteId:Guid?}/{blogId:Guid?}")]
        [Authorize(Policy = Permission.Posts)]
        public IActionResult Modal(Guid? siteId = null, Guid? blogId = null)
        {
            if (!siteId.HasValue)
            {
                var site = Request.Cookies[COOKIE_SELECTEDSITE];
                if (!string.IsNullOrEmpty(site))
                {
                    siteId = new Guid(site);
                }
            }
            return View(Models.PostModalModel.GetByBlogId(_api, siteId, blogId));
        }
    }
}
