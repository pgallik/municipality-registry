namespace MunicipalityRegistry.Api.Legacy.Municipality
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Mime;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Xml;
    using Be.Vlaanderen.Basisregisters.Api;
    using Be.Vlaanderen.Basisregisters.Api.Exceptions;
    using Be.Vlaanderen.Basisregisters.Api.Search;
    using Be.Vlaanderen.Basisregisters.Api.Search.Filtering;
    using Be.Vlaanderen.Basisregisters.Api.Search.Pagination;
    using Be.Vlaanderen.Basisregisters.Api.Search.Sorting;
    using Be.Vlaanderen.Basisregisters.Api.Syndication;
    using Be.Vlaanderen.Basisregisters.GrAr.Common;
    using Be.Vlaanderen.Basisregisters.GrAr.Common.Syndication;
    using Be.Vlaanderen.Basisregisters.GrAr.Legacy;
    using Convertors;
    using Infrastructure.Options;
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Options;
    using Microsoft.SyndicationFeed;
    using Microsoft.SyndicationFeed.Atom;
    using Projections.Legacy;
    using Query;
    using Requests;
    using Responses;
    using Swashbuckle.AspNetCore.Filters;
    using ProblemDetails = Be.Vlaanderen.Basisregisters.BasicApiProblem.ProblemDetails;

    [ApiVersion("1.0")]
    [AdvertiseApiVersions("1.0")]
    [ApiRoute("gemeenten")]
    [ApiExplorerSettings(GroupName = "Gemeenten")]
    public class MunicipalityController : ApiController
    {
        /// <summary>
        /// Vraag een gemeente op.
        /// </summary>
        /// <param name="context"></param>
        /// <param name="responseOptions"></param>
        /// <param name="nisCode">Identificator van de gemeente.</param>
        /// <param name="cancellationToken"></param>
        /// <response code="200">Als de gemeente gevonden is.</response>
        /// <response code="404">Als de gemeente niet gevonden kan worden.</response>
        /// <response code="500">Als er een interne fout is opgetreden.</response>
        [HttpGet("{nisCode}")]
        [ProducesResponseType(typeof(MunicipalityResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
        [SwaggerResponseExample(StatusCodes.Status200OK, typeof(MunicipalityResponseExamples))]
        [SwaggerResponseExample(StatusCodes.Status404NotFound, typeof(MunicipalityNotFoundResponseExamples))]
        [SwaggerResponseExample(StatusCodes.Status500InternalServerError, typeof(InternalServerErrorResponseExamples))]
        public async Task<IActionResult> Get(
            [FromServices] LegacyContext context,
            [FromServices] IOptions<ResponseOptions> responseOptions,
            [FromRoute] string nisCode,
            CancellationToken cancellationToken = default)
        {
            var municipality =
                await context
                    .MunicipalityDetail
                    .AsNoTracking()
                    .SingleOrDefaultAsync(item => item.NisCode == nisCode, cancellationToken);

            if (municipality == null)
            {
                throw new ApiException("Onbestaande gemeente.", StatusCodes.Status404NotFound);
            }

            return Ok(
                new MunicipalityResponse(
                    responseOptions.Value.Naamruimte,
                    municipality.Status.ConvertFromMunicipalityStatus(),
                    municipality.NisCode,
                    municipality.OfficialLanguages,
                    municipality.FacilitiesLanguages,
                    municipality.NameDutch,
                    municipality.NameFrench,
                    municipality.NameGerman,
                    municipality.NameEnglish,
                    municipality.VersionTimestamp.ToBelgianDateTimeOffset()));
        }

        /// <summary>
        /// Vraag een lijst met actieve gemeenten op.
        /// </summary>
        /// <param name="context"></param>
        /// <param name="responseOptions"></param>
        /// <param name="cancellationToken"></param>
        /// <response code="200">Als de opvraging van een lijst met gemeenten gelukt is.</response>
        /// <response code="500">Als er een interne fout is opgetreden.</response>
        [HttpGet]
        [ProducesResponseType(typeof(MunicipalityListResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
        [SwaggerResponseExample(StatusCodes.Status200OK, typeof(MunicipalityListResponseExamples))]
        [SwaggerResponseExample(StatusCodes.Status500InternalServerError, typeof(InternalServerErrorResponseExamples))]
        public async Task<IActionResult> List(
            [FromServices] LegacyContext context,
            [FromServices] IOptions<ResponseOptions> responseOptions,
            CancellationToken cancellationToken = default)
        {
            var filtering = Request.ExtractFilteringRequest<MunicipalityListFilter>();
            var sorting = Request.ExtractSortingRequest();
            var pagination = Request.ExtractPaginationRequest();

            var pagedMunicipalities = new MunicipalityListQuery(context).Fetch(filtering, sorting, pagination);

            Response.AddPagedQueryResultHeaders(pagedMunicipalities);

            return Ok(
                new MunicipalityListResponse
                {
                    Gemeenten = await pagedMunicipalities
                        .Items
                        .Select(m => new MunicipalityListItemResponse(
                            m.NisCode,
                            responseOptions.Value.Naamruimte,
                            responseOptions.Value.DetailUrl,
                            m.VersionTimestamp.ToBelgianDateTimeOffset(),
                            new GeografischeNaam(m.DefaultName, m.OfficialLanguages.FirstOrDefault().ConvertFromLanguage()),
                            m.Status))
                        .ToListAsync(cancellationToken),
                    Volgende = BuildNextUri(pagedMunicipalities.PaginationInfo, responseOptions.Value.VolgendeUrl)
                });
        }

        /// <summary>
        /// Vraag het totaal aantal van actieve gemeenten op.
        /// </summary>
        /// <param name="context"></param>
        /// <param name="cancellationToken"></param>
        /// <response code="200">Als de opvraging van het totaal aantal gelukt is.</response>
        /// <response code="500">Als er een interne fout is opgetreden.</response>
        [HttpGet("totaal-aantal")]
        [ProducesResponseType(typeof(TotaalAantalResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
        [SwaggerResponseExample(StatusCodes.Status200OK, typeof(TotalCountResponseExample))]
        [SwaggerResponseExample(StatusCodes.Status500InternalServerError, typeof(InternalServerErrorResponseExamples))]
        public async Task<IActionResult> Count(
            [FromServices] LegacyContext context,
            CancellationToken cancellationToken = default)
        {
            var filtering = Request.ExtractFilteringRequest<MunicipalityListFilter>();
            var sorting = Request.ExtractSortingRequest();
            var pagination = new NoPaginationRequest();

            return Ok(
                new TotaalAantalResponse
                {
                    Aantal = filtering.ShouldFilter
                        ? await new MunicipalityListQuery(context)
                            .Fetch(filtering, sorting, pagination)
                            .Items
                            .CountAsync(cancellationToken)
                        : await context
                            .MunicipalityList
                            .CountAsync(cancellationToken)
                });
        }

        /// <summary>
        /// Vraag een lijst met wijzigingen van gemeenten op.
        /// </summary>
        /// <param name="configuration"></param>
        /// <param name="context"></param>
        /// <param name="responseOptions"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        [HttpGet("sync")]
        [Produces("text/xml")]
        [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
        [SwaggerResponseExample(StatusCodes.Status200OK, typeof(MunicipalitySyndicationResponseExamples))]
        [SwaggerResponseExample(StatusCodes.Status400BadRequest, typeof(BadRequestResponseExamples))]
        [SwaggerResponseExample(StatusCodes.Status500InternalServerError, typeof(InternalServerErrorResponseExamples))]
        public async Task<IActionResult> Sync(
            [FromServices] IConfiguration configuration,
            [FromServices] LegacyContext context,
            [FromServices] IOptions<ResponseOptions> responseOptions,
            CancellationToken cancellationToken = default)
        {
            var filtering = Request.ExtractFilteringRequest<MunicipalitySyndicationFilter>();
            var sorting = Request.ExtractSortingRequest();
            var pagination = Request.ExtractPaginationRequest();

            var lastFeedUpdate = await context
                .MunicipalitySyndication
                .AsNoTracking()
                .OrderByDescending(item => item.Position)
                .Select(item => item.SyndicationItemCreatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            if (lastFeedUpdate == default)
            {
                lastFeedUpdate = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
            }

            var pagedMunicipalities =
                new MunicipalitySyndicationQuery(
                    context,
                    filtering.Filter?.Embed ?? new SyncEmbedValue())
                .Fetch(filtering, sorting, pagination);

            return new ContentResult
            {
                Content = await BuildAtomFeed(lastFeedUpdate, pagedMunicipalities, responseOptions, configuration),
                ContentType = MediaTypeNames.Text.Xml,
                StatusCode = StatusCodes.Status200OK
            };
        }

        /// <summary>
        /// Zoek naar gemeenten in het Vlaams Gewest in het BOSA formaat.
        /// </summary>
        /// <param name="context"></param>
        /// <param name="reponseOptions"></param>
        /// <param name="request">De request in BOSA formaat.</param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        [HttpPost("bosa")]
        [ProducesResponseType(typeof(MunicipalityBosaResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
        [SwaggerRequestExample(typeof(BosaMunicipalityRequest), typeof(MunicipalityBosaRequestExamples))]
        [SwaggerResponseExample(StatusCodes.Status200OK, typeof(MunicipalityBosaResponseExamples))]
        [SwaggerResponseExample(StatusCodes.Status400BadRequest, typeof(BadRequestResponseExamples))]
        [SwaggerResponseExample(StatusCodes.Status500InternalServerError, typeof(InternalServerErrorResponseExamples))]
        public async Task<IActionResult> Post(
            [FromServices] LegacyContext context,
            [FromServices] IOptions<ResponseOptions> reponseOptions,
            [FromBody] BosaMunicipalityRequest request,
            CancellationToken cancellationToken = default)
        {
            if (Request.ContentLength.HasValue && Request.ContentLength > 0 && request == null)
            {
                return Ok(new MunicipalityBosaResponse());
            }

            var filtering = new MunicipalityBosaFilter(request);
            var sorting = new SortingHeader(string.Empty, SortOrder.Ascending);
            var pagination = new PaginationRequest(0, 1000);

            var filteredMunicipalities = new MunicipalityBosaQuery(context).Fetch(
                new FilteringHeader<MunicipalityBosaFilter>(filtering),
                sorting,
                pagination);

            return Ok(
                new MunicipalityBosaResponse
                {
                    Gemeenten = await filteredMunicipalities
                        .Items
                        .Select(m =>
                            new MunicipalityBosaItemResponse(
                                m.NisCode,
                                reponseOptions.Value.Naamruimte,
                                m.Version,
                                GetGemeentenamenByLanguage(m, filtering.Language)))
                        .ToListAsync(cancellationToken)
                });
        }

        private static IEnumerable<GeografischeNaam> GetGemeentenamenByLanguage(MunicipalityBosaQueryResult municipality, Language? language)
        {
            var gemeenteNamen = new List<GeografischeNaam>();

            if (language.HasValue)
            {
                gemeenteNamen.Add(new GeografischeNaam(municipality.GetNameValueByLanguage(language.Value), (Taal)language.Value));
                return gemeenteNamen;
            }

            if (!string.IsNullOrEmpty(municipality.NameDutch))
            {
                gemeenteNamen.Add(new GeografischeNaam(municipality.NameDutch, Taal.NL));
            }

            if (!string.IsNullOrEmpty(municipality.NameFrench))
            {
                gemeenteNamen.Add(new GeografischeNaam(municipality.NameFrench, Taal.FR));
            }

            if (!string.IsNullOrEmpty(municipality.NameGerman))
            {
                gemeenteNamen.Add(new GeografischeNaam(municipality.NameGerman, Taal.DE));
            }

            if (!string.IsNullOrEmpty(municipality.NameEnglish))
            {
                gemeenteNamen.Add(new GeografischeNaam(municipality.NameEnglish, Taal.EN));
            }

            return gemeenteNamen;
        }

        private static async Task<string> BuildAtomFeed(
            DateTimeOffset lastUpdate,
            PagedQueryable<MunicipalitySyndicationQueryResult> pagedMunicipalities,
            IOptions<ResponseOptions> responseOptions,
            IConfiguration configuration)
        {
            var sw = new StringWriterWithEncoding(Encoding.UTF8);

            using (var xmlWriter = XmlWriter.Create(sw, new XmlWriterSettings { Async = true, Indent = true, Encoding = sw.Encoding }))
            {
                var formatter = new AtomFormatter(null, xmlWriter.Settings) { UseCDATA = true };
                var writer = new AtomFeedWriter(xmlWriter, null, formatter);
                var syndicationConfiguration = configuration.GetSection("Syndication");
                var atomConfiguration = AtomFeedConfigurationBuilder.CreateFrom(syndicationConfiguration, lastUpdate);

                await writer.WriteDefaultMetadata(atomConfiguration);

                var municipalities = await pagedMunicipalities.Items.ToListAsync();

                var highestPosition = municipalities.Any()
                    ? municipalities.Max(x => x.Position)
                    : (long?)null;

                var nextUri = BuildNextSyncUri(
                    pagedMunicipalities.PaginationInfo.Limit,
                    highestPosition + 1,
                    syndicationConfiguration["NextUri"]);

                if (nextUri != null)
                {
                    await writer.Write(new SyndicationLink(nextUri, GrArAtomLinkTypes.Next));
                }

                foreach (var municipality in municipalities)
                {
                    await writer.WriteMunicipality(responseOptions, formatter, syndicationConfiguration["Category"], municipality);
                }

                xmlWriter.Flush();
            }

            return sw.ToString();
        }

        private static Uri BuildNextUri(PaginationInfo paginationInfo, string nextUrlBase)
        {
            var offset = paginationInfo.Offset;
            var limit = paginationInfo.Limit;

            return paginationInfo.HasNextPage
                ? new Uri(string.Format(nextUrlBase, offset + limit, limit))
                : null;
        }

        private static Uri BuildNextSyncUri(int limit, long? from, string nextUrlBase)
        {
            return from.HasValue
                ? new Uri(string.Format(nextUrlBase, from.Value, limit))
                : null;
        }
    }
}
