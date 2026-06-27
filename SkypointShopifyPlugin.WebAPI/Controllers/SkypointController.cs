using MediatR;
using Microsoft.AspNetCore.Mvc;
using SkypointShopifyPlugin.Application.Common;
using SkypointShopifyPlugin.Application.Features.Skypoint.Commands;
using SkypointShopifyPlugin.Core.DTOs.Skypoint;

namespace SkypointShopifyPlugin.WebAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SkypointController : ControllerBase
    {
        private readonly IMediator _mediator;

        public SkypointController(IMediator mediator)
        {
            _mediator = mediator;
        }

        /// <summary>
        /// Login to Skypoint API
        /// </summary>
        [HttpPost("login")]
        public async Task<ActionResult<Response<LoginResponse>>> Login([FromBody] LoginCommand command)
        {
            var result = await _mediator.Send(command);
            if (!result.Succeeded)
                return StatusCode(result.StatusCode, result);

            return Ok(result);
        }

        /// <summary>
        /// Get shipping rates from Skypoint
        /// </summary>
        [HttpPost("rates")]
        public async Task<ActionResult<Response<List<RateResponse>>>> GetRates([FromBody] GetRatesCommand command)
        {
            var result = await _mediator.Send(command);
            if (!result.Succeeded)
                return StatusCode(result.StatusCode, result);

            return Ok(result);
        }

        /// <summary>
        /// Create a booking with Skypoint
        /// </summary>
        [HttpPost("booking")]
        public async Task<ActionResult<Response<BookingResponse>>> CreateBooking([FromBody] CreateBookingCommand command)
        {
            var result = await _mediator.Send(command);
            if (!result.Succeeded)
                return StatusCode(result.StatusCode, result);

            return Ok(result);
        }
    }
}
