﻿using System;
using Microsoft.AspNetCore.Mvc;
using AppServices;
using AppServices.Services;
using Web.ViewModels;
using Domain;
using Microsoft.AspNetCore.Authorization;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Web.Framework.Helpers.Alerts;
using Domain.Entities;
using AppServices.Framework;
using System.Linq;
using System.Collections.Generic;
using Web.Services.Slack;
using Newtonsoft.Json;
using Web.Models.Slack;
using System.Net;
using System.IO;
using System.Text;

namespace Web.Controllers
{
    public class JobsController : BaseController
    {
        private readonly IJobsService _jobsService;
        private readonly ICategoriesService _categoriesService;
        private readonly IHireTypesService _hiretypesService;
        private readonly ITwitterService _twitterService;
        private readonly LegacyApiClient _apiClient;
        private readonly IConfiguration _configuration;
        private readonly ICompaniesService _companiesService;
        private readonly ISlackService _slackService;

        public JobsController(IJobsService jobsService, ICategoriesService categoriesService, IHireTypesService hiretypesService, ITwitterService twitterService, LegacyApiClient apiClient, IConfiguration configuration, ICompaniesService companiesService, ISlackService slackService)
        {
            _jobsService = jobsService;
            _categoriesService = categoriesService;
            _hiretypesService = hiretypesService;
            _twitterService = twitterService;
            _apiClient = apiClient;
            _configuration = configuration;
            _companiesService = companiesService;
            _slackService = slackService;
        }

        public async Task<IActionResult> Index(JobSeachViewModel model)
        {

            /*
             var recentJobs = _jobsService.GetRecentJobs();

            var legacyJobs = await _apiClient.GetJobsFromLegacy();

            if(legacyJobs != null)
            {
                List<Job> legacyJobsTemp = new List<Job>();

                foreach (var legacyJob in legacyJobs)
                {
                    legacyJobsTemp.Add(new Job()
                    {
                        Company = new Company()
                        {
                            Name = legacyJob.CompanyName,
                            LogoUrl = legacyJob.CompanyLogoUrl,
                            Url = legacyJob.Link,
                            Email = legacyJob.Email
                        },
                        Title = legacyJob.Title,
                        PublishedDate = legacyJob.PublishedDate,
                        Description = legacyJob.Description,
                        HowToApply = legacyJob.HowToApply,
                        IsRemote = legacyJob.IsRemote,
                        ViewCount = legacyJob.ViewCount,
                        Likes = legacyJob.Likes,
                        Location = new Location
                        {
                            Name = legacyJob.Location
                        },
                        HireType = new HireType
                        {
                            Description = legacyJob.JobType
                        }
                    });
                }

                recentJobs = recentJobs.Concat(legacyJobsTemp).ToList();
            }
            */

            if (model == null)
            {
                model = new JobSeachViewModel();
            }

            bool? isOnlyRemotes = null;
            if (model.IsRemote)
                isOnlyRemotes = model.IsRemote;

            var jobs = _jobsService.Search(model.Keyword, model.CategoryId, model.HireTypeId, isOnlyRemotes);

            model.Jobs = jobs;

            model.Categories = _categoriesService.GetAll();
            model.HireTypes = _hiretypesService.GetAll();

            return View(model);
        }

        [Authorize]
        public IActionResult Wizard(int? id)
        {
            var model = new WizardViewModel
            {
                Categories = _categoriesService.GetAll(),
                JobTypes = _hiretypesService.GetAll(),
                Companies  = _companiesService.GetByUserId(_currentUser.UserId)
            };

            if (id.HasValue)
            {
                var originalJob = _jobsService.GetById(id.Value);
                if(originalJob.UserId == _currentUser.UserId)
                {
                    model.Id = originalJob.Id;
                    model.CompanyId = originalJob.Company.Id;
                    model.CreateNewCompany = false;
                    model.Title = originalJob.Title;
                    model.Description = originalJob.Description;
                    model.HowToApply = originalJob.HowToApply;
                    model.CategoryId = originalJob.CategoryId;
                    model.JobTypeId = originalJob.HireTypeId;
                    model.IsRemote = originalJob.IsRemote;
                    model.LocationName = originalJob.Location.Name;
                    model.LocationPlaceId = originalJob.Location.PlaceId;
                    model.LocationLatitude = originalJob.Location.Latitude;
                    model.LocationLongitude = originalJob.Location.Longitude;
                }
                else
                { 
                    return RedirectToAction("Index", "Home").WithError("No tienes permiso para editar esta posición");
                }
            }

            return View(model);
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> Wizard(WizardViewModel model)
        {
            model.Categories = _categoriesService.GetAll();
            model.JobTypes = _hiretypesService.GetAll();
            model.Companies = _companiesService.GetByUserId(_currentUser.UserId);

            if (ModelState.IsValid)
            {
                try
                {
                    var companyId = model.CompanyId;
                    if (model.CreateNewCompany)
                    {
                        var company = new Company
                        {
                            Name = model.CompanyName,
                            Url = model.CompanyUrl,
                            LogoUrl = model.CompanyLogoUrl,
                            UserId = _currentUser.UserId,
                            Email = model.CompanyEmail
                        };

                        _companiesService.Create(company);
                        companyId = company.Id;
                    }

                    if (model.Id.HasValue)
                    {
                        var originalJob = _jobsService.GetById(model.Id.Value);
                        if(originalJob.UserId == _currentUser.UserId)
                        {

                            originalJob.CategoryId = model.CategoryId;
                            originalJob.HireTypeId = model.JobTypeId;
                            originalJob.CompanyId = companyId.Value;
                            originalJob.HowToApply = model.HowToApply;
                            originalJob.Description = model.Description;
                            originalJob.Title = model.Title;
                            originalJob.IsRemote = model.IsRemote;
                            if(originalJob.Location.PlaceId != model.LocationPlaceId)
                            { 
                                originalJob.Location = new Location
                                {
                                    PlaceId = model.LocationPlaceId,
                                    Name = model.LocationName,
                                    Longitude = model.LocationLongitude,
                                    Latitude = model.LocationLatitude
                                };
                            }
                            var result = _jobsService.Update(originalJob);
                            if (result.Success)
                            {
                                await _slackService.PostJob(originalJob, Url);
                                return RedirectToAction("Wizard", new { Id = model.Id.Value }).WithSuccess("Posición editada exitosamente");
                            }

                            return View(model).WithError(result.Messages);
                        }
                        else
                        {
                            return RedirectToAction("Index", "Home").WithError("No tienes permiso para editar esta posición");
                        }
                    }
                    else
                    {
                        var newJob = new Job
                        {
                            CategoryId = model.CategoryId,
                            HireTypeId = model.JobTypeId,
                            CompanyId = companyId.Value,
                            HowToApply = model.HowToApply,
                            Description = model.Description,
                            Title = model.Title,
                            IsRemote = model.IsRemote,
                            Location = new Location
                            {
                                PlaceId = model.LocationPlaceId,
                                Name = model.LocationName,
                                Longitude = model.LocationLongitude,
                                Latitude = model.LocationLatitude
                            },
                            UserId = _currentUser.UserId,
                            IsHidden = false,
                            IsApproved = false
                        };
                        var result = _jobsService.Create(newJob);
                        if (result.Success)
                        {
                            await _slackService.PostJob(newJob, Url).ConfigureAwait(false);

                            return RedirectToAction("Details", new { newJob.Id, isPreview = true }).WithInfo(result.Messages);
                        }

                        return View(model).WithError(result.Messages);
                    }

                }
                catch(Exception ex)
                {
                    return View(model).WithError(ex.Message);
                }
            }
            return View(model);
        }

        public async Task<IActionResult> Details(string Id, bool isPreview = false, bool isLegacy = false)
        {
            if (String.IsNullOrEmpty(Id))
                return RedirectToAction(nameof(this.Index));

            int jobId = Int32.Parse(Id);
            Job job = new Job();
            if (isLegacy)
            {
                var legacyJob = await _apiClient.GetJobById(Id);
                if(legacyJob != null) { 
                    job = new Job()
                    {
                        Company = new Company()
                        {
                            Name = legacyJob.CompanyName,
                            LogoUrl = legacyJob.CompanyLogoUrl,
                            Url = legacyJob.Link,
                            Email = legacyJob.Email
                        },
                        Title = legacyJob.Title,
                        PublishedDate = legacyJob.PublishedDate,
                        Description = legacyJob.Description,
                        HowToApply = legacyJob.HowToApply,
                        IsRemote = legacyJob.IsRemote,
                        ViewCount = legacyJob.ViewCount,
                        Likes = legacyJob.Likes,
                        Location = new Location
                        {
                            Name = legacyJob.Location
                        },
                        HireType = new HireType
                        {
                            Description = legacyJob.JobType
                        }
                    };
                }
            }
            else
            {
               job = this._jobsService.GetDetails(jobId, isPreview);
            }

            //Manage error message
            if (job == null)
                return RedirectToAction(nameof(this.Index)).WithError("El puesto que buscas no existe.");

            //If reach this line is because the job exists
            var viewModel = new JobDetailsViewModel
            {   
                Job = job,
                IsJobOwner = (job.UserId == _currentUser.UserId)
            };

            if(!isLegacy)
            { 
                job.ViewCount++;
                _jobsService.Update(job);
            }

            if (isPreview)
            {
                viewModel.IsPreview = isPreview;
                return View(viewModel);
            }
            return View(viewModel);
        }

        private int GetJobIdFromTitle(string title)
        {
            var url = title.Split('-');
            if (String.IsNullOrEmpty(title) || title.Length == 0 || !int.TryParse(url[0], out int id))
                return 0;
            return id;
        }

        [Authorize]
        [HttpPost]
        public JsonResult Hide(int id)
        {
            var result = new TaskResult();
            try
            {
                var job = _jobsService.GetById(id);
                if (job == null)
                {
                    result.AddErrorMessage("No puedes esconder un puesto que no existe.");
                }
                else if (job.UserId == _currentUser.UserId)
                {
                    job.IsHidden = !job.IsHidden;
                    result = _jobsService.Update(job);
                }
                else
                {
                    result.AddErrorMessage("No puedes esconder un puesto que no creaste.");
                }
            }
            catch (Exception ex)
                        {
                result.AddErrorMessage(ex.Message);
            }
            return Json(result);
        }
        
        [Authorize]
        [HttpPost]
        public JsonResult Delete(int id)
        {
            var result = new TaskResult();
            try
            {
                var job = _jobsService.GetById(id);

                if(job == null)
                {
                    result.AddErrorMessage("No puedes eliminar un puesto que no existe.");
                }
                else if(job.UserId == _currentUser.UserId)
                {
                    if (!job.IsActive)
                    {
                        result.AddErrorMessage("El puesto que intentas eliminar ya está eliminado.");
                    }
                    else
                    { 
                        result = _jobsService.Delete(job);
                    }
                }
                else
                {
                    result.AddErrorMessage("No puedes eliminar un puesto que no creaste.");
                }
            }
            catch(Exception ex)
            {
                result.AddErrorMessage(ex.Message);
            }
            return Json(result);
        }


        /// <summary>
        /// Validates the payload response that comes from the Slack interactive message actions
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>s
        [HttpPost]
        //[ValidateInput(false)]
        public async Task Validate()
        {
            var bodyStr = "";
            using (StreamReader reader = new StreamReader(Request.Body, Encoding.UTF8))
            {
                bodyStr = await reader.ReadToEndAsync();
            }

            var payload = JsonConvert.DeserializeObject<PayloadResponseDto>(bodyStr);
            int jobOpportunityId = Convert.ToInt32(payload.callback_id);
            var jobOpportunity = _jobsService.GetById(jobOpportunityId);
            var isJobApproved = payload.actions.FirstOrDefault()?.value == "approve";
            var isJobRejected = payload.actions.FirstOrDefault()?.value == "reject";
            var isTokenValid = payload.token == _configuration["Slack:VerificationToken"];

            try
            {
                if (isTokenValid && isJobApproved)
                {
                    jobOpportunity.IsApproved = true;
                    jobOpportunity.PublishedDate = DateTime.UtcNow;
                    _jobsService.Update(jobOpportunity);
                    await _slackService.PostJobResponse(jobOpportunity, Url, payload.response_url, payload?.user?.id, true);
                }
                else if (isTokenValid && isJobRejected)
                {
                    // Jobs are rejected by default, so there's no need to update the DB
                    if (jobOpportunity == null)
                    {
                        await _slackService.PostJobErrorResponse(jobOpportunity, Url, payload.response_url);
                    }
                    else
                    {
                        await _slackService.PostJobResponse(jobOpportunity, Url, payload.response_url, payload?.user?.id, false);
                    }
                }
                else
                {
                    Response.StatusCode = (int)HttpStatusCode.BadRequest;
                }
            }
            catch (Exception ex)
            {
                //Catches exceptions so that the raw HTML doesn't appear on the slack channel
              //  await _slackService.PostJobOpportunityErrorResponse(jobOpportunity, Url, payload.response_url);
            }
        }

    }
}
