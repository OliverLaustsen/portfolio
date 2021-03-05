using Abbyy.CloudOcrSdk;
using KTBackend.Attributes;
using KTBackend.Models;
using KTBackend.Models.Campaign;
using KTBackend.Models.ERP;
using KTBackend.Models.Report;
using KTBackend.Models.Search;
using KTBackend.Models.Valizo;
using LinqKit;
using Mandrill;
using Mandrill.Model;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using Ninject;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Linq.Dynamic.Core;
using System.Reactive.Subjects;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using WampSharp.V2;
using WampSharp.V2.Authentication;
using WampSharp.V2.Core.Contracts;
using WampSharp.V2.Rpc;
using KTBackend.Models.ERP.ProductSpecifications;
using KTBackend.Models.TTI;
using KTBackend.Models.KK;
using SparkDotNet;

namespace KTBackend
{
    public class WampContract
    {
        private const string ROUTERPATH = @"C:\Crossbar\uploads\";
        private const string VALIZOFILEPATH = @"C:\Valizo\orders\";
        private const string CISCOTOKEN = @"ZjE5YTJmMjgtNGEzMi00YjAyLWExOTAtYjU5ZWU0ZjQ0NWY1OTgzZjgwYzQtMjUx";

        private readonly IWampRealmServiceProvider service;
        private readonly ICrossbarMetaProcedures procedures;
        //private readonly IDatabase database;
        private readonly ILogger log;

        private readonly WampSharp.V2.Client.IWampRealmProxy proxy;

        private Timer ttiImportTimer;

        private DateTime lastSynchronisation;

        private IDatabase _testDatabase;
        private IDatabase testDatabase
        {
            get
            {
                if( _testDatabase == null || _testDatabase.IsConnected == false )
                {
                    _testDatabase = DIUtils.Kernel.Get<IDatabase>();
#if VALIZO
                    if( !_testDatabase.Connect( "valizo" ) )
#elif TEST
                    if( !_testDatabase.Connect( "test" ) )
#elif TTI
                    if( !_testDatabase.Connect( "tti" ) )
#elif BACKENDDEV
                    if( !_testDatabase.Connect( "backendDev" ) )
#elif BACKENDDEVMARTIN
                    if( !_testDatabase.Connect( "backendDevMartin" ) )
#elif BACKENDDEVMAGAARD
                    if( !_testDatabase.Connect( "backendDevMagaard" ) )
#elif BACKENDDEVOLIVER
                    if( !_testDatabase.Connect( "backendDevOliver" ) )
#elif BACKENDDEVMATHIAS
                    if( !_testDatabase.Connect( "backendDevMathias" ) )
#elif KK
                    if( !_testDatabase.Connect( "kk" ) )
#else
                    if( !_testDatabase.Connect( "dev" ) )
#endif
                        throw new InvalidOperationException( "ERROR! Failed to connect to test database" );
                }

                return _testDatabase;
            }
        }

        public async Task<string> GetInvocationAuthId()
        {
            if( WampInvocationContext.Current == null || WampInvocationContext.Current.InvocationDetails == null || string.IsNullOrEmpty( WampInvocationContext.Current.InvocationDetails.CallerAuthenticationId ) )
            {
                var metaSession = await procedures.GetWampSession( WampInvocationContext.Current.InvocationDetails.Caller.Value );
                return metaSession?.AuthId ?? string.Empty;
            }

            return WampInvocationContext.Current.InvocationDetails.CallerAuthenticationId;
        }

        public async Task<string> GetInvocationAuthRole()
        {
            if( WampInvocationContext.Current == null || WampInvocationContext.Current.InvocationDetails == null || string.IsNullOrEmpty( WampInvocationContext.Current.InvocationDetails.CallerAuthenticationId ) )
            {
                var metaSession = await procedures.GetWampSession( WampInvocationContext.Current.InvocationDetails.Caller.Value );
                return metaSession?.AuthRole ?? string.Empty;
            }

            return WampInvocationContext.Current.InvocationDetails.CallerAuthenticationRole;
        }

        public IDatabase Database
        {
            get
            {
                //service.GetCalleeProxy
                //TODO: Implement proper callee identification such that it is possible to determine the database name based on the authenticationId
                return testDatabase;
                //WampInvocationContext.Current.InvocationDetails.AuthenticationId;
            }
        }

        private IDatabase _userDatabase;
        public IDatabase UserDatabase
        {
            get
            {
                if( _userDatabase == null || !_userDatabase.IsConnected )
                {
                    _userDatabase = DIUtils.Kernel.Get<IDatabase>();
                    if( !_userDatabase.Connect( "user" ) )
                        throw new InvalidOperationException( "ERROR! Failed to connect to user database" );
                }

                return _userDatabase;
            }
        }

        IDisposable nodispo;

        public WampContract( IWampRealmServiceProvider service, ILogger log, WampSharp.V2.Client.IWampRealmProxy proxy )
        {
            if( service == null )
                throw new ArgumentNullException( "service" );

            this.service = service;
            //this.database = database;
            this.procedures = service.GetCalleeProxy<ICrossbarMetaProcedures>();
            this.log = log;

            this.proxy = proxy;

            this.nodispo = this.OnUploadFinished.Subscribe( x =>
            {
                ValizoFileUploaded( x );
            } );

            //Recreate tasks for PriceSchedules that have yet to be executed
            var priceScheduleRep = this.Database.Get<PriceSchedule>();
            foreach( var schedule in priceScheduleRep.Query.Where( x => !x.Executed ) )
            {
                priceScheduleExecuteCreateTimer( schedule );
            }

#if TTI && !DEBUG 
            lastSynchronisation = DateTime.Now;
            var finishedLast = true;
            ttiImportTimer = new Timer( x =>
            {
                if( !finishedLast )
                    return;
                var tempLastSynchronisation = lastSynchronisation;
                lastSynchronisation = DateTime.Now;
                DebugImportTTIData( tempLastSynchronisation );
            }, null, TimeSpan.Zero, TimeSpan.FromMinutes( 10d ) );
#endif
        }

        #region Events

        #region Valizo Events

        private ISubject<Airport> _onValizoAirportAdded;
        public ISubject<Airport> OnValizoAirportAdded
        {
            get
            {
                return _onValizoAirportAdded ?? ( _onValizoAirportAdded = service.GetSubject<Airport>( "erp.valizo.airport.events.onadded" ) );
            }
            set
            {
                _onValizoAirportAdded = value;
            }
        }

        private ISubject<Airport> _onValizoAirportUpdated;
        public ISubject<Airport> OnValizoAirportUpdated
        {
            get
            {
                return _onValizoAirportUpdated ?? ( _onValizoAirportUpdated = service.GetSubject<Airport>( "erp.valizo.airport.events.onupdated" ) );
            }
            set
            {
                _onValizoAirportUpdated = value;
            }
        }

        private ISubject<string> _onValizoAirportDeleted;
        public ISubject<string> OnValizoAirportDeleted
        {
            get
            {
                return _onValizoAirportDeleted ?? ( _onValizoAirportDeleted = service.GetSubject<string>( "erp.valizo.airport.events.ondeleted" ) );
            }
            set
            {
                _onValizoAirportDeleted = value;
            }
        }

        #endregion

        #region KK Events

        private ISubject<SIPMember> _onSIPMemberAdded;
        public ISubject<SIPMember> OnSIPMemberAdded
        {
            get
            {
                return _onSIPMemberAdded ?? ( _onSIPMemberAdded = service.GetSubject<SIPMember>( "kk.sip.sipmember.events.onadded" ) );
            }
            set
            {
                _onSIPMemberAdded = value;
            }
        }

        private ISubject<SIPMember> _onSIPMemberUpdated;
        public ISubject<SIPMember> OnSIPMemberUpdated
        {
            get
            {
                return _onSIPMemberUpdated ?? ( _onSIPMemberUpdated = service.GetSubject<SIPMember>( "kk.sip.sipmember.events.onupdated" ) );
            }
            set
            {
                _onSIPMemberUpdated = value;
            }
        }

        private ISubject<string> _onSIPMemberDeleted;
        public ISubject<string> OnSIPMemberDeleted
        {
            get
            {
                return _onSIPMemberDeleted ?? ( _onSIPMemberDeleted = service.GetSubject<string>( "kk.sip.sipmember.events.ondeleted" ) );
            }
            set
            {
                _onSIPMemberDeleted = value;
            }
        }

        #endregion

        private ISubject<ProductBrand> _onProductBrandAdded;
        public ISubject<ProductBrand> OnProductBrandAdded
        {
            get
            {
                return _onProductBrandAdded ?? ( _onProductBrandAdded = service.GetSubject<ProductBrand>( "erp.inventory.productbrand.events.onadded" ) );
            }
            set
            {
                _onProductBrandAdded = value;
            }
        }

        private ISubject<ProductBrand> _onProductBrandUpdated;
        public ISubject<ProductBrand> OnProductBrandUpdated
        {
            get
            {
                return _onProductBrandUpdated ?? ( _onProductBrandUpdated = service.GetSubject<ProductBrand>( "erp.inventory.productbrand.events.onupdated" ) );
            }
            set
            {
                _onProductBrandUpdated = value;
            }
        }

        private ISubject<string> _onProductBrandDeleted;
        public ISubject<string> OnProductBrandDeleted
        {
            get
            {
                return _onProductBrandDeleted ?? ( _onProductBrandDeleted = service.GetSubject<string>( "erp.inventory.productbrand.events.ondeleted" ) );
            }
            set
            {
                _onProductBrandDeleted = value;
            }
        }

        private ISubject<Assortment> _onAssortmentAdded;
        public ISubject<Assortment> OnAssortmentAdded
        {
            get
            {
                return _onAssortmentAdded ?? ( _onAssortmentAdded = service.GetSubject<Assortment>( "erp.inventory.assortment.events.onadded" ) );
            }
            set
            {
                _onAssortmentAdded = value;
            }
        }

        private ISubject<Assortment> _onAssortmentUpdated;
        public ISubject<Assortment> OnAssortmentUpdated
        {
            get
            {
                return _onAssortmentUpdated ?? ( _onAssortmentUpdated = service.GetSubject<Assortment>( "erp.inventory.assortment.events.onupdated" ) );
            }
            set
            {
                _onAssortmentUpdated = value;
            }
        }

        private ISubject<string> _onAssortmentDeleted;
        public ISubject<string> OnAssortmentDeleted
        {
            get
            {
                return _onAssortmentDeleted ?? ( _onAssortmentDeleted = service.GetSubject<string>( "erp.inventory.assortment.events.ondeleted" ) );
            }
            set
            {
                _onAssortmentDeleted = value;
            }
        }

        private ISubject<PriceSchedule> _onPriceScheduleAdded;
        public ISubject<PriceSchedule> OnPriceScheduleAdded
        {
            get
            {
                return _onPriceScheduleAdded ?? ( _onPriceScheduleAdded = service.GetSubject<PriceSchedule>( "erp.inventory.product.priceschedule.events.onadded" ) );
            }
            set
            {
                _onPriceScheduleAdded = value;
            }
        }

        private ISubject<PriceSchedule> _onPriceScheduleUpdated;
        public ISubject<PriceSchedule> OnPriceScheduleUpdated
        {
            get
            {
                return _onPriceScheduleUpdated ?? ( _onPriceScheduleUpdated = service.GetSubject<PriceSchedule>( "erp.inventory.product.priceschedule.events.onupdated" ) );
            }
            set
            {
                _onPriceScheduleUpdated = value;
            }
        }

        private ISubject<string> _onPriceScheduleDeleted;
        public ISubject<string> OnPriceScheduleDeleted
        {
            get
            {
                return _onPriceScheduleDeleted ?? ( _onPriceScheduleDeleted = service.GetSubject<string>( "erp.inventory.product.priceschedule.events.ondeleted" ) );
            }
            set
            {
                _onPriceScheduleDeleted = value;
            }
        }

        private ISubject<DiscountBase> _onDiscountAdded;
        public ISubject<DiscountBase> OnDiscountAdded
        {
            get
            {
                return _onDiscountAdded ?? ( _onDiscountAdded = service.GetSubject<DiscountBase>( "erp.inventory.discount.events.onadded" ) );
            }
            set
            {
                _onDiscountAdded = value;
            }
        }

        private ISubject<DiscountBase> _onDiscountUpdated;
        public ISubject<DiscountBase> OnDiscountUpdated
        {
            get
            {
                return _onDiscountUpdated ?? ( _onDiscountUpdated = service.GetSubject<DiscountBase>( "erp.inventory.discount.events.onupdated" ) );
            }
            set
            {
                _onDiscountUpdated = value;
            }
        }

        private ISubject<string> _onDiscountDeleted;
        public ISubject<string> OnDiscountDeleted
        {
            get
            {
                return _onDiscountDeleted ?? ( _onDiscountDeleted = service.GetSubject<string>( "erp.inventory.discount.events.ondeleted" ) );
            }
            set
            {
                _onDiscountDeleted = value;
            }
        }

        private ISubject<Supplier> _onSupplierAdded;
        public ISubject<Supplier> OnSupplierAdded
        {
            get
            {
                return _onSupplierAdded ?? ( _onSupplierAdded = service.GetSubject<Supplier>( "erp.inventory.supplier.events.onsupplieradded" ) );
            }
            set
            {
                _onSupplierAdded = value;
            }
        }

        private ISubject<Supplier> _onSupplierUpdated;
        public ISubject<Supplier> OnSupplierUpdated
        {
            get
            {
                return _onSupplierUpdated ?? ( _onSupplierUpdated = service.GetSubject<Supplier>( "erp.inventory.supplier.events.onsupplierupdated" ) );
            }
            set
            {
                _onSupplierUpdated = value;
            }
        }

        private ISubject<string> _onSupplierDeleted;
        public ISubject<string> OnSupplierDeleted
        {
            get
            {
                return _onSupplierDeleted ?? ( _onSupplierDeleted = service.GetSubject<string>( "erp.inventory.supplier.events.onsupplierdeleted" ) );
            }
            set
            {
                _onSupplierDeleted = value;
            }
        }

        private ISubject<Product> _onProductAdded;
        public ISubject<Product> OnProductAdded
        {
            get
            {
                return _onProductAdded ?? ( _onProductAdded = service.GetSubject<Product>( "erp.inventory.product.events.onproductadded" ) );
            }
            set
            {
                _onProductAdded = value;
            }
        }

        private ISubject<Product> _onProductUpdated;
        public ISubject<Product> OnProductUpdated
        {
            get
            {
                return _onProductUpdated ?? ( _onProductUpdated = service.GetSubject<Product>( "erp.inventory.product.events.onproductupdated" ) );
            }
            set
            {
                _onProductUpdated = value;
            }
        }

        private ISubject<string> _onProductDeleted;
        public ISubject<string> OnProductDeleted
        {
            get
            {
                return _onProductDeleted ?? ( _onProductDeleted = service.GetSubject<string>( "erp.inventory.product.events.onproductdeleted" ) );
            }
            set
            {
                _onProductDeleted = value;
            }
        }

        private ISubject<ProductGroup> _onProductGroupAdded;
        public ISubject<ProductGroup> OnProductGroupAdded
        {
            get
            {
                return _onProductGroupAdded ?? ( _onProductGroupAdded = service.GetSubject<ProductGroup>( "erp.inventory.productgroup.events.onadded" ) );
            }
            set
            {
                _onProductGroupAdded = value;
            }
        }

        private ISubject<ProductGroup> _onProductGroupUpdated;
        public ISubject<ProductGroup> OnProductGroupUpdated
        {
            get
            {
                return _onProductGroupUpdated ?? ( _onProductGroupUpdated = service.GetSubject<ProductGroup>( "erp.inventory.productgroup.events.onupdated" ) );
            }
            set
            {
                _onProductGroupUpdated = value;
            }
        }

        private ISubject<string> _onProductGroupDeleted;
        public ISubject<string> OnProductGroupDeleted
        {
            get
            {
                return _onProductGroupDeleted ?? ( _onProductGroupDeleted = service.GetSubject<string>( "erp.inventory.productgroup.events.ondeleted" ) );
            }
            set
            {
                _onProductGroupDeleted = value;
            }
        }

        private ISubject<ProductSpecification> _onProductSpecificationAdded;
        public ISubject<ProductSpecification> OnProductSpecificationAdded
        {
            get
            {
                return _onProductSpecificationAdded ?? ( _onProductSpecificationAdded = service.GetSubject<ProductSpecification>( "erp.inventory.productspecification.events.onadded" ) );
            }
            set
            {
                _onProductSpecificationAdded = value;
            }
        }

        private ISubject<ProductSpecification> _onProductSpecificationUpdated;
        public ISubject<ProductSpecification> OnProductSpecificationUpdated
        {
            get
            {
                return _onProductSpecificationUpdated ?? ( _onProductSpecificationUpdated = service.GetSubject<ProductSpecification>( "erp.inventory.productspecification.events.onupdated" ) );
            }
            set
            {
                _onProductSpecificationUpdated = value;
            }
        }

        private ISubject<string> _onProductSpecificationDeleted;
        public ISubject<string> OnProductSpecificationDeleted
        {
            get
            {
                return _onProductSpecificationDeleted ?? ( _onProductSpecificationDeleted = service.GetSubject<string>( "erp.inventory.productspecification.events.ondeleted" ) );
            }
            set
            {
                _onProductSpecificationDeleted = value;
            }
        }

        private ISubject<Unit> _onUnitAdded;
        public ISubject<Unit> OnUnitAdded
        {
            get
            {
                return _onUnitAdded ?? ( _onUnitAdded = service.GetSubject<Unit>( "erp.unit.events.onadded" ) );
            }
            set
            {
                _onUnitAdded = value;
            }
        }

        private ISubject<Unit> _onUnitUpdated;
        public ISubject<Unit> OnUnitUpdated
        {
            get
            {
                return _onUnitUpdated ?? ( _onUnitUpdated = service.GetSubject<Unit>( "erp.unit.events.onupdated" ) );
            }
            set
            {
                _onUnitUpdated = value;
            }
        }

        private ISubject<string> _onUnitDeleted;
        public ISubject<string> OnUnitDeleted
        {
            get
            {
                return _onUnitDeleted ?? ( _onUnitDeleted = service.GetSubject<string>( "erp.unit.events.ondeleted" ) );
            }
            set
            {
                _onUnitDeleted = value;
            }
        }

        private ISubject<Category> _onCategoryAdded;
        public ISubject<Category> OnCategoryAdded
        {
            get
            {
                return _onCategoryAdded ?? ( _onCategoryAdded = service.GetSubject<Category>( "erp.inventory.category.events.onadded" ) );
            }
            set
            {
                _onCategoryAdded = value;
            }
        }

        private ISubject<Category> _onCategoryUpdated;
        public ISubject<Category> OnCategoryUpdated
        {
            get
            {
                return _onCategoryUpdated ?? ( _onCategoryUpdated = service.GetSubject<Category>( "erp.inventory.category.events.onupdated" ) );
            }
            set
            {
                _onCategoryUpdated = value;
            }
        }

        private ISubject<string> _onCategoryDeleted;
        public ISubject<string> OnCategoryDeleted
        {
            get
            {
                return _onCategoryDeleted ?? ( _onCategoryDeleted = service.GetSubject<string>( "erp.inventory.category.events.ondeleted" ) );
            }
            set
            {
                _onCategoryDeleted = value;
            }
        }

        private ISubject<Customer> _onCustomerAdded;
        public ISubject<Customer> OnCustomerAdded
        {
            get
            {
                return _onCustomerAdded ?? ( _onCustomerAdded = service.GetSubject<Customer>( "erp.crm.customer.events.oncustomeradded" ) );
            }
            set
            {
                _onCustomerAdded = value;
            }
        }

        private ISubject<Customer> _onCustomerUpdated;
        public ISubject<Customer> OnCustomerUpdated
        {
            get
            {
                return _onCustomerUpdated ?? ( _onCustomerUpdated = service.GetSubject<Customer>( "erp.crm.customer.events.oncustomerupdated" ) );
            }
            set
            {
                _onCustomerUpdated = value;
            }
        }

        private ISubject<string> _onCustomerDeleted;
        public ISubject<string> OnCustomerDeleted
        {
            get
            {
                return _onCustomerDeleted ?? ( _onCustomerDeleted = service.GetSubject<string>( "erp.crm.customer.events.oncustomerdeleted" ) );
            }
            set
            {
                _onCustomerDeleted = value;
            }
        }

        private ISubject<Campaign> _onCampaignAdded;
        public ISubject<Campaign> OnCampaignAdded
        {
            get
            {
                return _onCampaignAdded ?? ( _onCampaignAdded = service.GetSubject<Campaign>( "erp.inventory.campaign.events.oncampaignadded" ) );
            }
            set
            {
                _onCampaignAdded = value;
            }
        }

        private ISubject<Campaign> _onCampaignUpdated;
        public ISubject<Campaign> OnCampaignUpdated
        {
            get
            {
                return _onCampaignUpdated ?? ( _onCampaignUpdated = service.GetSubject<Campaign>( "erp.inventory.campaign.events.oncampaignupdated" ) );
            }
            set
            {
                _onCampaignUpdated = value;
            }
        }

        private ISubject<string> _onCampaignDeleted;
        public ISubject<string> OnCampaignDeleted
        {
            get
            {
                return _onCampaignDeleted ?? ( _onCampaignDeleted = service.GetSubject<string>( "erp.inventory.campaign.events.oncampaigndeleted" ) );
            }
            set
            {
                _onCampaignDeleted = value;
            }
        }

        private ISubject<User> _onUserAdded;
        public ISubject<User> OnUserAdded
        {
            get
            {
                return _onUserAdded ?? ( _onUserAdded = service.GetSubject<User>( "erp.user.events.onadded" ) );
            }
            set
            {
                _onUserAdded = value;
            }
        }

        private ISubject<User> _onUserUpdated;
        public ISubject<User> OnUserUpdated
        {
            get
            {
                return _onUserUpdated ?? ( _onUserUpdated = service.GetSubject<User>( "erp.user.events.onupdated" ) );
            }
            set
            {
                _onUserUpdated = value;
            }
        }

        private ISubject<string> _onUserDeleted;
        public ISubject<string> OnUserDeleted
        {
            get
            {
                return _onUserDeleted ?? ( _onUserDeleted = service.GetSubject<string>( "erp.user.events.ondeleted" ) );
            }
            set
            {
                _onUserDeleted = value;
            }
        }

        private ISubject<object> _onUploadProgress;
        public ISubject<object> OnUploadProgress
        {
            get
            {
                return _onUploadProgress ?? ( _onUploadProgress = service.GetSubject<object>( "erp.file.upload.events.onprogress" ) );
            }
            set
            {
                _onUploadProgress = value;
            }
        }

        private ISubject<FileUpload> _onUploadFinished;
        public ISubject<FileUpload> OnUploadFinished
        {
            get
            {
                return _onUploadFinished ?? ( _onUploadFinished = service.GetSubject<FileUpload>( "erp.file.upload.events.onuploadfinished" ) );
            }
            set
            {
                _onUploadFinished = value;
            }
        }

        #endregion

        #region Scandic

        /*[WampProcedure( "erp.scandic.todo.getstate" )]
        public async Task<List<Todo>> GetTodoState( DateTime? date = null )
        {
            List<Todo> todos = null;
            if( date == null || !date.HasValue || date == DateTime.MinValue || date == DateTime.MaxValue )
            {
                var repository = await database.GetAsync<Todo>();
                todos = await repository.Query.ToListAsync();
            }
            else
            {
                var repository = await database.GetAsync<Todo>();
                //TODO: Change to not use ToAsyncEnumerable?
                todos = await repository.Query.ToAsyncEnumerable().Where( x =>
                {
                    return x.Expires.Date == date.Value.Date && !x.Finished;
                } ).ToList();
            }

            return todos;
        }

        [WampProcedure( "erp.scandic.todo.addtodo" )]
        public async Task<Todo> AddTodo( Todo todo, EmployeeGroup group )
        {
            //Console.WriteLine( "{0} called", nameof( AddTodo ) );
            log.LogDebug( Event.DEBUGENTEREDFUNCTION, $"{this}.AddTodo() entered" );
            if( todo == null )
                throw new ArgumentNullException( "todo" );
            if( group == null )
                throw new ArgumentNullException( "group" );

            log.LogInformation( $"Executing database.get with {todo}" );

            //TODO: remove
            var possibleEmployeeGroups = new EmployeeGroup[]
            {
                new EmployeeGroup() { Name = "Receptionen" },
                new EmployeeGroup() { Name = "Housekeeping" },
                new EmployeeGroup() { Name = "Teknisk Afdeling" },
                new EmployeeGroup() { Name = "Konference tjener" }
            };

            if( !possibleEmployeeGroups.Select( x => x.Name ).Any( x => x.Equals( group.Name, StringComparison.CurrentCultureIgnoreCase ) ) )
                todo.EmployeeGroup = possibleEmployeeGroups[ new Random().Next( 4 ) ];
            //^

            var repository = await database.GetAsync<Todo>();
            await repository.InsertAsync( todo );

            OnTodoAdded.OnNext( new OnTodoAddedEvent() { Todo = todo, EmployeeGroup = todo.EmployeeGroup } );

            log.LogDebug( Event.DEBUGFINISHEDFUNCTION, $"{this}.AddTodo() finished" );
            //Console.WriteLine( "AddTodo() finished" );
            return todo;
        }

        [WampProcedure( "erp.scandic.todo.finishtodo" )]
        public async Task FinishTodo( string id )
        {
            log.LogDebug( Event.DEBUGENTEREDFUNCTION, $"{this}.FinishTodo() entered" );
            if( string.IsNullOrEmpty( id ) )
                throw new ArgumentNullException( "id" );

            log.LogInformation( $"Executing todo.get with {id}" );
            var repository = await database.GetAsync<Todo>();

            var todo = await repository.Query.FirstOrDefaultAsync( x => x.Id == id );
            if( todo == null )
                throw new WampException( new Dictionary<string, object>(), "erp.scandic.todo.error.invalidid", $"ERROR! No todo with the id: '{ id }'", null );

            if( todo.Finished )
                throw new WampException( new Dictionary<string, object>(), "erp.scandic.todo.error.alreadyfinished", $"ERROR! The todo with the id: '{ id }' is already finished", null );

            todo.Finished = true;

            log.LogInformation( $"Executing database.update with {todo}" );
            await repository.UpdateAsync( todo );

            OnTodoFinished.OnNext( todo );

            log.LogDebug( Event.DEBUGFINISHEDFUNCTION, $"{this}.FinishTodo() finished" );
        }

        [WampProcedure( "erp.scandic.todo.removetodo" )]
        public async Task RemoveTodo( string id )
        {
            log.LogDebug( Event.DEBUGENTEREDFUNCTION, $"{this}.RemoveTodo() entered" );

            if( id == null )
                throw new ArgumentNullException( "id" );

            var rep = database.Get<Todo>();

            var todo = await rep.Query.FirstOrDefaultAsync( x => x.Id.Equals( id ) );
            if( todo == null )
                throw new WampException( new Dictionary<string, object>(), "erp.scandic.todo.error.invalidid", $"ERROR! No todo with the id: '{ id }'", null );
            //throw new WampException( "erp.scandoc.todo.error.invalidid", $"ERROR! No todo with the id: '{ id }'" );


            await rep.DeleteAsync( id );

            OnTodoRemoved.OnNext( todo );

            log.LogDebug( Event.DEBUGFINISHEDFUNCTION, $"{this}.RemoveTodo() finished" );
        }

        //TODO: Change so it does NOT use a username but an employee
        [WampProcedure( "erp.scandic.todo.assigntodo" )]
        public async Task AssignTodo( string id, string username )
        {
            var todoRepo = database.Get<Todo>();

            var todo = await todoRepo.Query.FirstOrDefaultAsync( x => x.Id.Equals( id ) );
            if( todo == null )
                throw new WampException( new Dictionary<string, object>(), "erp.scandic.todo.error.invalidid", $"ERROR! No todo with the id: '{ id }'", null );

            var userRepo = userDatabase.Get<User>();

            var user = await userRepo.Query.FirstOrDefaultAsync( x => x.Username.Equals( username, StringComparison.CurrentCultureIgnoreCase ) );
            if( user == null )
                throw new WampException( new Dictionary<string, object>(), "erp.scandic.user.error.invalidid", $"ERROR! No user with the id: '{ username }'", null );

            todo.Employee = new Employee() { FirstName = username, EmployeeGroups = user.EmployeeGroups };

            await todoRepo.UpdateAsync( todo );

            OnTodoAssigned.OnNext( new OnTodoAssignedEvent() { Todo = todo, User = user } );
        }

        [WampProcedure( "erp.scandic.conference.getstate" )]
        public async Task<List<Conference>> GetConferenceState()
        {
            var repository = await database.GetAsync<Conference>();
            var conferences = await repository.Query.ToListAsync();

            return conferences;
        }

        [WampProcedure( "erp.scandic.conference.add" )]
        public async Task AddConference( Conference conference )
        {
            log.LogDebug( Event.DEBUGENTEREDFUNCTION, $"{this}.AddConference() entered" );

            if( conference == null )
                throw new ArgumentNullException( "conference" );
            if( !string.IsNullOrEmpty( conference.Id ) )
                throw new WampException( new Dictionary<string, object>(), "erp.scandic.conference.error.invalidid", $"ERROR! Cannot add a new conference with an already assigned id", null );

            var rep = database.Get<Conference>();

            var existingConferenceInTimespan = rep.Query.Where( x => x.Room.Id == conference.Room.Id ).Any( x => x.StartTime > conference.StartTime && x.EndTime < conference.StartTime );
            if( existingConferenceInTimespan )
                throw new WampException( new Dictionary<string, object>(), "erp.scandic.conference.error.invalidtime", $"ERROR! A conference already exists between that time span", null );

            await rep.InsertAsync( conference );

            OnConferenceAdded.OnNext( conference );

            log.LogDebug( Event.DEBUGFINISHEDFUNCTION, $"{this}.AddConference() finished" );
        }

        [WampProcedure( "erp.scandic.conference.remove" )]
        public async Task RemoveConference( string id )
        {
            log.LogDebug( Event.DEBUGENTEREDFUNCTION, $"{this}.RemoveConference() entered" );

            if( id == null )
                throw new ArgumentNullException( "id" );

            var rep = database.Get<Conference>();

            var conference = await rep.Query.FirstOrDefaultAsync( x => x.Id.Equals( id ) );
            if( conference == null )
                throw new WampException( new Dictionary<string, object>(), "erp.scandic.conference.error.invalidid", $"ERROR! No conference with the id: '{ id }'", null );

            await rep.DeleteAsync( id );

            OnConferenceRemoved.OnNext( conference );

            log.LogDebug( Event.DEBUGFINISHEDFUNCTION, $"{this}.RemoveConference() finished" );
        }*/

        #endregion

        [WampProcedure( "erp.user.login" )]
        public async Task<Dictionary<string, string>> Login( string realm, string authid, CrossbarSession details )
        {
            if( string.IsNullOrEmpty( authid ) )
                throw new WampAuthenticationException( "ERROR! Cannot authenticate request with empty authid." );

            var res = new Dictionary<string, string>();

            var rep = UserDatabase.Get<User>();
            var user = await rep.Query.FirstOrDefaultAsync( x => x.Username == authid );
            if( user == null )
                throw new WampException( new Dictionary<string, object>(), "erp.user.error.invalidusername", $"ERROR! No user found with the username :'{ authid }", null );

            if( string.IsNullOrEmpty( realm ) || realm.Equals( "none", StringComparison.OrdinalIgnoreCase ) )
            {
                if( user.DefaultUnit == null || string.IsNullOrEmpty( user.DefaultUnit?.Id ) )
                    throw new WampAuthenticationException( "ERROR! Cannot authenticate request with empty realm and no realm defined on DefaultStore" );
                else
                    res.Add( "realm", user.DefaultUnit.Resolve<Unit>( Database ).Realm );
            }

            res.Add( "secret", user.Password.Value );
            res.Add( "role", "client" );

            if( !string.IsNullOrEmpty( user.Password.Salt ) )
            {
                res.Add( "salt", user.Password.Salt );
                res.Add( "iterations", user.Password.Iterations.ToString() );
                res.Add( "keylen", user.Password.KeyLength.ToString() );
            }

            return res;
        }

        #region User

        [WampProcedure( "erp.user.get" )]
        public async Task<User> UserGet( string Id )
        {
            log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.UserGet() entered" );

            var rep = UserDatabase.Get<User>();
            var authId = await GetInvocationAuthId();
            var invocatingUser = await rep.Query.FirstOrDefaultAsync( x => x.Username == authId );
            if( invocatingUser == null )
                throw new Exception( "ERROR! Failed to find invocating user in the user database" );

            //Check if admin
            User user = null;
            if( !invocatingUser.IsAdmin )
            {
                if( !Id.Equals( invocatingUser.Id ) )
                    throw new Exception( "ERROR! Not enough permission to find other users" );

                user = invocatingUser;
            }
            else
            {
                //Don't allow admins to find users from other units
                var foundUser = await rep.Query.FirstOrDefaultAsync( x => x.Id == Id );
                if( foundUser == null || foundUser.DefaultUnit.Id != invocatingUser.DefaultUnit?.Id )
                    throw new Exception( $"ERROR! No user found with the id '{ Id }' or the user is in another unit" );

                user = foundUser;
            }

            log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.UserGet() finished" );

            return user;
        }

        [WampProcedure( "erp.user.getlist" )]
        public async Task<List<User>> UserGetList( bool onlySelf = false )
        {
            log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.UserGetList() entered" );

            var rep = UserDatabase.Get<User>();
            var authId = await GetInvocationAuthId();
            var user = await rep.Query.FirstOrDefaultAsync( x => x.Username == authId );
            if( user == null )
                throw new Exception( "ERROR! Failed to find invocating user in the user database" );

            //Check if admin
            List<User> users = null;
            if( !user.IsAdmin || onlySelf || user.DefaultUnit == null )
            {
                users = new List<User>() { user };
            }
            else
            {
                var unitId = user.DefaultUnit.Id;
                users = await rep.Query.WhereAsync( x => x.DefaultUnit.Id == unitId );
            }

            log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.UserGetList() finished" );

            return users;
        }

        [WampProcedure( "erp.user.create" )]
        public async Task UserCreate( User user )
        {
            log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.UserCreate() entered" );

            //user = new User() { Username = "scandesign", Password = new Password() { Value = "pYEfW7QF_S87HdKdfPJz" }, DefaultUnit = new DBRef( "58e12ed669179d03f8da1332" ) };

            if( user == null )
                throw new ArgumentNullException( "user" );
            if( string.IsNullOrEmpty( user.Username ) )
                throw new ArgumentNullException( "user.Username" );
            if( string.IsNullOrEmpty( user.Password.Value ) )
                throw new ArgumentNullException( "user.Password.Value" );

            var unitRepo = Database.Get<Unit>();
            if( user.DefaultUnit == null )
            {
                //TODO: Remove, change to throw WampException();
                var existingTTIUnit = await unitRepo.Query.FirstOrDefaultAsync( x => x.Name == "TTI" );
                if( existingTTIUnit == null )
                {
                    var newUnit = new Unit() { Name = "TTI", Realm = "TTI" };
                    await unitRepo.InsertAsync( newUnit );
                    user.DefaultUnit = new DBRef( newUnit.Id );
                }
                else
                    user.DefaultUnit = new DBRef( existingTTIUnit.Id );
            }

            var rep = UserDatabase.Get<User>();
            log.LogTrace( Event.VARIABLEGET, $"{this}.UserCreate(): looking for existing user with username: '{user.Username}' and password: '{user.Password.ToStringPretty()}" );
            var existingUser = await rep.Query.FirstOrDefaultAsync( x => x.Username == user.Username );
            if( existingUser != null )
                throw new Exception( $"ERROR! A user with the username: '{user.Username}' already exists" );

            log.LogTrace( Event.VARIABLEGET, $"{this}.UserCreate(): looking for existing unit with id: '{user.DefaultUnit.Id}'" );
            var existingUnit = await unitRepo.Query.FirstOrDefaultAsync( x => x.Id == user.DefaultUnit.Id );
            if( existingUnit == null )
                throw new Exception( $"ERROR! No unit with the id: '{user.DefaultUnit.Id}' exists" );
            else
            {
                log.LogDebug( Event.VARIABLEVALIDATED, $"{this}.UserCreate(): no existing user found and found existing unit. Attempting to insert new user..." );

                try
                {
                    await rep.InsertAsync( user );

                    OnUserAdded.OnNext( user );
                }
                catch( Exception e )
                {
                    log.LogError( Event.EXCEPTIONCAUGHT, e, "ERROR! Failed to insert new user into database" );
                }
            }

            log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.UserCreate() finished" );
        }

        [WampProcedure( "erp.user.update" )]
        public async Task UserUpdate( User user )
        {
            log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.UpdateUser() entered" );

            if( user == null || string.IsNullOrEmpty( user.Id ) )
                throw new ArgumentNullException( "user" );

            var rep = UserDatabase.Get<User>();
            log.LogTrace( Event.VARIABLEGET, $"{this}.CreateUser(): looking for existing user with id: '{user.Id}'" );
            var existingUser = await rep.Query.FirstOrDefaultAsync( x => x.Id == user.Id );
            if( existingUser == null )
                throw new WampException( new Dictionary<string, object>(), "erp.user.error.invalidid", $"ERROR! No existing user with the id: '{user.Id}'", null );
            if( user.Password == null || string.IsNullOrEmpty( user.Password.Value ) )
            {
                if( existingUser.Password == null || string.IsNullOrEmpty( existingUser.Password.Value ) )
                    throw new Exception( $"ERROR! User.Password must be a non-empty string" );
                else
                    user.Password = existingUser.Password;
            }
            long parsedLong;
            if( !long.TryParse( user.Password.Value, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out parsedLong ) )
                throw new Exception( $"ERROR! User.Password must be a valid SHA256 string" );

            var authId = await GetInvocationAuthId();
            var invocatingUser = ( await rep.Query.FirstOrDefaultAsync( x => x.Username == authId ) );
            if( invocatingUser.IsAdmin )
            {
                if( !existingUser.DefaultUnit.Id.Equals( invocatingUser.DefaultUnit.Id, StringComparison.OrdinalIgnoreCase ) )
                    throw new Exception( "ERROR! Not allowed to change user objects of other units" );
            }
            else if( !existingUser.Id.Equals( user.Id, StringComparison.OrdinalIgnoreCase ) )
            {
                throw new Exception( "ERROR! Unable to change user object of another user, without being admin" );
            }

            try
            {
                user.DefaultUnit = existingUser.DefaultUnit;
                user.IsAdmin = existingUser.IsAdmin;

                await rep.ReplaceAsync( user );

                OnUserUpdated.OnNext( user );
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, "ERROR! Failed to update user in database" );
                throw;
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.UpdateUser() finished" );
            }
        }

        [WampProcedure( "erp.user.exists" )]
        public async Task<bool> UserExists( string username )
        {
            if( string.IsNullOrEmpty( username ) )
                throw new ArgumentNullException( "username" );

            var rep = UserDatabase.Get<User>();
            log.LogTrace( Event.VARIABLEGET, $"{this}.UserExists(): looking for existing user with username: '{username}'" );
            var existingUser = await rep.Query.FirstOrDefaultAsync( x => x.Username == username );
            if( existingUser != null )
                return true;
            else
                return false;
        }

        #endregion

        #region Report

        [WampProcedure( "erp.report.get" )]
        public async Task<Report> ReportGet( string reportName, Dictionary<string, string> reportOptions )
        {
            try
            {
                log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.ReportGet() entered" );

                Report report = null;

                using( var conn = new SqlConnection( @"Data Source=94.189.39.182,49225;Initial Catalog=SDBOSSQL;User ID=testtest;Password=@magerT0rvAdmin" ) )
                {
                    await conn.OpenAsync();

                    SqlCommand command = null;

                    switch( reportName )
                    {
                        case "Dagsrapport":
                            {
                                command = new SqlCommand( "NewBossReportDailyReport", conn );

                                string optionValue;
                                if( !reportOptions.TryGetValue( "FromDateTime", out optionValue ) )
                                    throw new Exception( "ERROR! No value for option 'FromDateTime' given" );
                                command.Parameters.Add( "@FromDateTime", System.Data.SqlDbType.DateTime );
                                command.Parameters[ "@FromDateTime" ].Value = DateTime.ParseExact( optionValue, "yyyy-M-d", System.Globalization.CultureInfo.InvariantCulture );
                                if( !reportOptions.TryGetValue( "ToDateTime", out optionValue ) )
                                    throw new Exception( "ERROR! No value for option 'ToDateTime' given" );
                                command.Parameters.Add( "@ToDateTime", System.Data.SqlDbType.DateTime );
                                command.Parameters[ "@ToDateTime" ].Value = DateTime.ParseExact( optionValue, "yyyy-M-d", System.Globalization.CultureInfo.InvariantCulture );
                            }
                            break;
                        case "SalgOverTid":
                            {
                                command = new SqlCommand( "NewBossSalesOverTime", conn );

                                string optionValue;
                                if( !reportOptions.TryGetValue( "FromDateTime", out optionValue ) )
                                    throw new Exception( "ERROR! No value for option 'FromDateTime' given" );
                                command.Parameters.Add( "@FromDateTime", System.Data.SqlDbType.DateTime );
                                command.Parameters[ "@FromDateTime" ].Value = DateTime.ParseExact( optionValue, "yyyy-M-d", System.Globalization.CultureInfo.InvariantCulture );
                                if( !reportOptions.TryGetValue( "ToDateTime", out optionValue ) )
                                    throw new Exception( "ERROR! No value for option 'ToDateTime' given" );
                                command.Parameters.Add( "@ToDateTime", System.Data.SqlDbType.DateTime );
                                command.Parameters[ "@ToDateTime" ].Value = DateTime.ParseExact( optionValue, "yyyy-M-d", System.Globalization.CultureInfo.InvariantCulture );
                            }
                            break;
                        default:
                            break;
                    }

                    command.CommandType = System.Data.CommandType.StoredProcedure;

                    using( command )
                    using( var reader = await command.ExecuteReaderAsync() )
                    {
                        switch( reportName )
                        {
                            case "Dagsrapport":
                                {


                                    /*var headers = new Header[] { new Header() { Name = "Butik nummer" }, new Header() { Name = "Butik navn" }, new Header() { Name = "Antal Eksp" }, new Header() { Name = "Salg inkl moms" } };
                                    var row1 = new Row() { Cells = new[] { new Cell() { Value = "1" }, new Cell() { Value = "Tårnby Torv" }, new Cell() { Value = "95" }, new Cell() { Value = "17.925,83" } } };
                                    var row2 = new Row() { Cells = new[] { new Cell() { Value = "2" }, new Cell() { Value = "AC" }, new Cell() { Value = "120" }, new Cell() { Value = "14.981,52" } } };
                                    var row3 = new Row() { Cells = new[] { new Cell() { Value = "3" }, new Cell() { Value = "Gør Det Selv" }, new Cell() { Value = "118" }, new Cell() { Value = "12.678,05" } } };
                                    var row4 = new Row() { BorderTop = true, Cells = new[] { new Cell() { Value = "" }, new Cell() { Value = "" }, new Cell() { Value = "333" }, new Cell() { Value = "45.585,40" } } };

                                    return report = new Report()
                                    {
                                        ReportFields = new[]
                                        {
                                            new ReportField() { Label = "Udskrevet den:", Text = DateTime.Now.ToString("dd-MM-yyyy HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) },
                                            new ReportField() { Label = "Fra:", Text = reportOptions["FromDateTime"] },
                                            new ReportField() { Label = "Til:", Text = reportOptions["ToDateTime"] },
                                            new ReportField() { Label = "Rapport navn:", Text = "test" }
                                        },
                                        Headers = headers,
                                        Rows = new[] { row1, row2, row3, row4 }
                                    };*/

                                    var headers = new Header[] { new Header() { Name = "Butik nummer" }, new Header() { Name = "Butik navn" }, new Header() { Name = "Antal Eksp" }, new Header() { Name = "Salg inkl moms" } };

                                    List<Row> rows = new List<Row>();
                                    while( await reader.ReadAsync() )
                                    {
                                        var row = new Row();
                                        row.Cells = new[] { new Cell() { Value = reader[ "ButikNr" ].ToString() }, new Cell() { Value = reader[ "ButikNavn" ] as string }, new Cell() { Value = reader[ "AntalEksp" ].ToString() }, new Cell() { Value = ( reader[ "BetalTotalt" ] as decimal? )?.ToString( "0.##" ) } };
                                        rows.Add( row );
                                    }

                                    return report = new Report()
                                    {
                                        ReportFields = new[]
                                        {
                                        new ReportField() { Label = "Udskrevet den:", Text = DateTime.Now.ToString("dd-MM-yyyy HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) },
                                        new ReportField() { Label = "Fra:", Text = reportOptions["FromDateTime"] },
                                        new ReportField() { Label = "Til:", Text = reportOptions["ToDateTime"] },
                                        new ReportField() { Label = "Rapport navn:", Text = "test" }
                                    },
                                        Headers = headers,
                                        Rows = rows.ToArray()
                                    };
                                }
                            case "SalgOverTid":
                                {
                                    var headers = new Header[] { new Header() { Name = "Butik nummer" }, new Header() { Name = "Butik navn" }, new Header() { Name = "Salg inkl moms" }, new Header() { Name = "Dato" } };

                                    List<Row> rows = new List<Row>();
                                    while( await reader.ReadAsync() )
                                    {
                                        var row = new Row();
                                        row.Cells = new[] { new Cell() { Value = reader[ "ButikNr" ].ToString() }, new Cell() { Value = reader[ "ButikNavn" ] as string }, new Cell() { Value = ( reader[ "BetalTotalt" ] as decimal? )?.ToString( "0.##" ) }, new Cell() { Value = ( reader[ "Dato" ] as DateTime? )?.ToString( "yyyy-M-d" ) } };
                                        rows.Add( row );
                                    }

                                    var uniqueIds = rows.Select( x => new
                                    {
                                        StoreNumber = x.Cells[ 0 ].Value,
                                        StoreName = x.Cells[ 1 ].Value
                                    } ).Distinct().ToList();
                                    var uniqueCount = uniqueIds.Count();
                                    var notCorrectCountGrouping = rows.GroupBy( x => x.Cells[ 3 ].Value ).Where( x => x.Count() != uniqueCount ).ToList();
                                    foreach( var row in notCorrectCountGrouping )
                                    {
                                        foreach( var rowMissingId in uniqueIds.Except( row.Select( x => new
                                        {
                                            StoreNumber = x.Cells[ 0 ].Value,
                                            StoreName = x.Cells[ 1 ].Value
                                        } ) ) )
                                        {
                                            rows.Add( new Row() { Cells = new[] { new Cell() { Value = rowMissingId.StoreNumber }, new Cell() { Value = rowMissingId.StoreName }, new Cell() { Value = "0" }, new Cell() { Value = row.Key } } } );
                                        }
                                    }

                                    return report = new Report()
                                    {
                                        ReportFields = new[]
                                        {
                                        new ReportField() { Label = "Udskrevet den:", Text = DateTime.Now.ToString("dd-MM-yyyy HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) },
                                        new ReportField() { Label = "Fra:", Text = reportOptions["FromDateTime"] },
                                        new ReportField() { Label = "Til:", Text = reportOptions["ToDateTime"] },
                                        new ReportField() { Label = "Rapport navn:", Text = "test" }
                                    },
                                        Headers = headers,
                                        Rows = rows.ToArray()
                                    };
                                }
                            default:
                                {
                                    var infoBox = new InfoBox() { Title = "Test", Subtitle = "Test2", Texts = new string[] { "hej", "med", "digdigdigdigdigdigdigdigdigdigdigdigdigdigdigdigdigdigdigdigdigdigdigdigdigdigdigdigdigdigdigdigdigdigdig" } };
                                    var reportField = new ReportField() { Label = "Referencenummer", Text = "Ingen ved det" };
                                    var headers = new Header[] { new Header() { Name = "Produkt" }, new Header() { Name = "Solgt" }, new Header() { Name = "Købt for" }, new Header() { Name = "Solgt for" } };
                                    var row = new Row() { Cells = new[] { new Cell() { Title = "Titanium Eel", Value = "It's an eel... made of titanium... get it?" }, new Cell() { Value = "42" }, new Cell() { Value = "42.000" }, new Cell() { Value = "84.000" } } };
                                    var row2 = new Row() { Cells = new[] { new Cell() { Title = "Titanium Eel", Value = "It's an eel... made of titanium... get it?" }, new Cell() { Value = "42" }, new Cell() { Value = "42.000" }, new Cell() { Value = "84.000" } }, BorderBottom = true };
                                    var row3 = new Row() { Cells = new[] { new Cell() { Title = "Titanium Eel", Value = "It's an eel... made of titanium... get it?" }, new Cell() { Value = "42" }, new Cell() { Value = "42.000" }, new Cell() { Value = "84.000" } }, BorderTop = true };

                                    return report = new Report() { Headers = headers, InfoBoxes = new InfoBox[] { infoBox }, ReportFields = new ReportField[] { reportField, reportField, reportField, reportField, new ReportField() { Label = "Rapport navn:", Text = "test" } }, Rows = new Row[] { row3, row3, row, row, row, row2, row3, row2, row, row, row3 } };
                                }
                                /*default:
                                    throw new Exception( $"ERROR! Unknown report type with name of '{reportName}'" );*/
                        }
                    }
                }
            }
            catch( WampException )
            {
                throw;
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, $"{this}.ReportGet(): failed to get report" );
                throw new WampException( "erp.error.internalservererror", "ERROR! An internal server error occured" );
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.ReportGet() finished" );
            }
        }

        [WampProcedure( "erp.report.type.getlist" )]
        public async Task<ReportType[]> ReportTypeGetList()
        {
            return new[] { new ReportType() { Name = "Dagsrapport" } };
        }

        [WampProcedure( "erp.report.type.getoptions" )]
        public async Task<ReportOption[]> ReportTypeGetOptions( string reportName )
        {
            switch( reportName )
            {
                case "Dagsrapport":
                    {
                        return new[]
                        {
                            new ReportOption() { Name = "FromDateTime", Type = EReportOptionType.DateTime, MinimumDate = new DateTime(DateTime.Now.Year, 1, 1), MaximumDate = DateTime.Now },
                            new ReportOption() { Name = "ToDateTime", Type = EReportOptionType.DateTime, MinimumDate = new DateTime(DateTime.Now.Year, 1, 1), MaximumDate = DateTime.Now }
                        };
                    }
                case "Test":
                    {
                        return null;
                    }
                default:
                    throw new Exception( $"ERROR! Unknown report type with name of '{reportName}'" );
            }
        }

        #endregion

        public class GetStateResult<T>
        {
            public List<T> Result
            {
                get;
                set;
            }

            public int FilterCount
            {
                get;
                set;
            }
        }

        public async Task<GetStateResult<T>> GetObjectState<T>( int first = 0, int count = 0, bool sortOrderAscending = true, string sortField = "Id", List<FilterData> filters = null, bool resolveReferences = false, bool test = false, bool includeList = true, IEnumerable<string> idList = null, IDatabase database = null ) where T : ModelBase
        {
            log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.GetObjectState() entered for type '{ typeof( T ).Name }'" );

            var rep = ( database != null ? database : Database ).Get<T>();

            var foundSortProperty = typeof( T ).GetPropertyOrField( sortField );
            if( foundSortProperty == null )
                throw new WampException( "erp.error.invalidsortfield", $"ERROR! No property or field found on '{typeof( T ).Name}' by the name '{sortField}'" );

            IMongoQueryable<T> query = null;
            if( !test )
                query = (IMongoQueryable<T>) rep.Query;
            else
                query = ( (IMongoQueryable<T>) rep.Query ).Where( x => x is PackageDiscount || x is QuantityDiscount );

            /*IEnumerable<T> query = null;
            if( !test )
                query = rep.Query;
            else
                query = rep.Query.Where( x => x is PackageDiscount || x is FamilyDiscount || x is QuantityDiscount );*/

            /*Func<T, object> orderDelegate = ( ( x ) =>
              ( foundSortProperty is PropertyInfo ? ( foundSortProperty as PropertyInfo ).GetValue( x ) : ( foundSortProperty as FieldInfo ).GetValue( x ) ) );*/
            //query = (IMongoQueryable<T>) query.AsExpandable().OrderBy( orderDelegate );
            //query = ( sortOrderAscending ? query.OrderBy( x => foundSortProperty ) : query.OrderByDescending( x => orderDelegate(x) ) );

            //query = ( sortOrderAscending ? ( query ).OrderBy( x => x.Id ) : query.OrderByDescending( x => x.Id ) );

            //var o = Builders<T>.Sort.Ascending( new StringFieldDefinition<T>( sortField ) );

            try
            {
                if( idList != null && idList.Any() )
                {
                    MongoDB.Driver.FilterDefinitionBuilder<T> filterBuilder = Builders<T>.Filter;
                    List<FilterDefinition<T>> excludeIncludeFilters = new List<FilterDefinition<T>>();

                    if( includeList )
                    {
                        var filterList = new List<FilterDefinition<T>>();
                        foreach( var item in idList )
                        {
                            filterList.Add( filterBuilder.Eq( "Id", item ) );
                        }
                        excludeIncludeFilters.Add( filterBuilder.Or( filterList ) );
                    }
                    else
                    {
                        foreach( var item in idList )
                        {
                            excludeIncludeFilters.Add( filterBuilder.Ne( "Id", item ) );
                        }
                    }

                    query = query.Where( x => filterBuilder.And( excludeIncludeFilters ).Inject() );
                }

                if( filters != null && filters.Count > 0 )
                {
                    MongoDB.Driver.FilterDefinitionBuilder<T> filterBuilder = Builders<T>.Filter;
                    List<FilterDefinition<T>> queryFilters = new List<FilterDefinition<T>>();
                    var orFilters = new List<FilterDefinition<T>>();

                    var finalFilter = filterBuilder.Empty;

                    for( int i = 0; i < filters.Count; i++ )
                    {
                        var filter = filters[ i ];

                        if( typeof( T ).IsAssignableFrom( typeof( DiscountBase ) ) && filters.Any( x => x.FieldName == "Number" ) )
                        {
                            finalFilter = filterBuilder.And( new[] { finalFilter, filterBuilder.Eq( "DiscountType", 1 ) } );
                            continue;
                        }

                        if( typeof( T ).GetPropertyOrField( filter.FieldName ).GetUnderlyingType() == typeof( DBRef ) || typeof( T ).GetPropertyOrField( filter.FieldName ).GetUnderlyingType() == typeof( DBRef[] ) )
                        {
                            if( filter.FieldName == "Supplier" )
                            {
                                var toLowerValue = filter.Value.ToLower();

                                IMongoQueryable<T> tquery = null;
                                if( typeof( T ) == typeof( Product ) )
                                {
                                    tquery = (IMongoQueryable<T>) ( Database.Get<Supplier>().Query as IMongoQueryable<Supplier> )
                                    .Where( x => x.CompanyNameLower.StartsWith( toLowerValue ) )
                                    .GroupJoin( Database.Get<Product>().Query, x => x.Id, y => y.Supplier.Id, ( x, y ) => new
                                    {
                                        Supplier = x.Id,
                                        Products = y
                                    } )
                                    .SelectMany( x => x.Products );
                                }
                                else
                                {
                                    throw new WampException( "erp.error.notimplemented", $"ERROR! Searching for Supplier on {typeof( T ).Name} isn't implemented" );
                                }

                                if( first > 0 )
                                    tquery = tquery.Skip( first );
                                if( count > 0 )
                                    tquery = tquery.Take( count );

                                var tresult = tquery.ToList();

                                if( tresult == null || tresult.Count == 0 )
                                    return new GetStateResult<T>() { FilterCount = 0, Result = new List<T>() };

                                var filterList = new List<FilterDefinition<T>>();
                                foreach( var item in tresult )
                                {
                                    filterList.Add( filterBuilder.Eq( "Id", item.Id ) );
                                }

                                if( filter.IsAnd )
                                    finalFilter = filterBuilder.And( new[] { finalFilter, filterBuilder.Or( filterList ) } );
                                else
                                    orFilters.Add( filterBuilder.Or( filterList ) );
                            }
                            else if( filter.FieldName == "Category" )
                            {
                                var filteredCategories = ( Database.Get<Category>().Query as IMongoQueryable<Category> )
                                    .Where( x => x.Name.ToLower().StartsWith( filter.Value.ToLower() ) )
                                    .Select( x => x.Id )
                                    .ToList();

                                var tquery = ( query as IMongoQueryable<Product> ).Where( x => x.Category.Any( y => filteredCategories.Contains( y.Id ) ) ).ToList();

                                var tresult = tquery.ToList();

                                if( tresult == null || tresult.Count == 0 )
                                    return new GetStateResult<T>() { FilterCount = 0, Result = new List<T>() };

                                var filterList = new List<FilterDefinition<T>>();
                                foreach( var item in tresult )
                                {
                                    filterList.Add( filterBuilder.Eq( "Id", item.Id ) );
                                }

                                if( filter.IsAnd )
                                    finalFilter = filterBuilder.And( new[] { finalFilter, filterBuilder.Or( filterList ) } );
                                else
                                    orFilters.Add( filterBuilder.Or( filterList ) );
                            }
                            else
                            {
                                throw new WampException( "erp.error.notimplemented", $"ERROR! {typeof( T ).Name} does not have a filter implementation for {filter.FieldName}" );
                            }
                        }
                        else
                        {
                            var newFilter = filterBuilder.Empty;

                            switch( filter.MatchMode )
                            {
                                case FilterMatchMode.Contains:
                                    newFilter = filterBuilder.Regex( filter.FieldName, new MongoDB.Bson.BsonRegularExpression( $"{filter.Value}", "i" ) );
                                    break;
                                case FilterMatchMode.Equals:
                                    newFilter = filterBuilder.Eq( filter.FieldName, filter.Value );
                                    break;
                                case FilterMatchMode.StartsWith:
                                    newFilter = filterBuilder.Regex( filter.FieldName, new MongoDB.Bson.BsonRegularExpression( $"^{filter.Value}", "i" ) );
                                    break;
                                case FilterMatchMode.EndsWith:
                                    newFilter = filterBuilder.Regex( filter.FieldName, new MongoDB.Bson.BsonRegularExpression( $"{filter.Value}$", "i" ) );
                                    break;
                                default:
                                    break;
                            }

                            if( filter.IsAnd )
                                finalFilter = filterBuilder.And( new[] { finalFilter, newFilter } );
                            else
                                orFilters.Add( newFilter );
                        }
                    }

                    query = query.Where( x => ( orFilters.Count > 0 ? ( filterBuilder.And( finalFilter, filterBuilder.Or( orFilters ) ) ) : finalFilter ).Inject() );
                }

                var totalRecords = await query.CountAsync();

                if( typeof( T ) == typeof( Campaign ) || typeof( T ).IsAssignableToGenericType( typeof( DiscountBase ) ) )
                    query = (IMongoQueryable<T>) query.Select( x => new Campaign( x.Id )
                    {
                        CreatedBy = x.CreatedBy,
                        CreatedDateTime = x.CreatedDateTime,
                        Deleted = x.Deleted,
                        DeletedBy = x.DeletedBy,
                        DeletedDateTime = x.DeletedDateTime,
                        DiscountType = ( x as Campaign ).DiscountType,
                        EndDateTime = ( x as Campaign ).EndDateTime,
                        ModifiedBy = x.ModifiedBy,
                        ModifiedDateTime = x.ModifiedDateTime,
                        Name = ( x as Campaign ).Name,
                        Number = ( x as Campaign ).Number,
                        Percentage = ( x as Campaign ).Percentage,
                        StartDateTime = ( x as Campaign ).StartDateTime
                    } );
                else if( typeof( T ) == typeof( PriceSchedule ) )
                    query = (IMongoQueryable<T>) query.Select( x => new PriceSchedule( x.Id )
                    {
                        CreatedBy = x.CreatedBy,
                        CreatedDateTime = x.CreatedDateTime,
                        Deleted = x.Deleted,
                        DeletedBy = x.DeletedBy,
                        DeletedDateTime = x.DeletedDateTime,
                        ModifiedBy = x.ModifiedBy,
                        ModifiedDateTime = x.ModifiedDateTime,
                        Campaign = ( x as PriceSchedule ).Campaign,
                        Executed = ( x as PriceSchedule ).Executed,
                        FromDateTime = ( x as PriceSchedule ).FromDateTime,
                        Items = ( x as PriceSchedule ).Items,
                        Name = ( x as PriceSchedule ).Name
                    } );

                if( sortOrderAscending )
                    query = (IMongoQueryable<T>) query.OrderBy( sortField, false );
                else
                    query = (IMongoQueryable<T>) query.OrderBy( sortField, true );

                if( first > 0 )
                    query = query.Skip( first );
                if( count > 0 )
                    query = query.Take( count );


                var result = await query.ToListAsync();

                return new GetStateResult<T>() { FilterCount = totalRecords, Result = result };
            }
            catch( WampException )
            {
                throw;
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, "ERROR! An exception occured while attempting to get state from database" );
                throw new Exception( "ERROR! An internal server error occured" );
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.GetObjectState() finished for type '{ typeof( T ).Name }'" );
            }

            try
            {
                //if( filters != null && filters.Count > 0 )
                {
                    //Create a dictionary of the Fields or Properties based on the Filterdata.Fieldname so we know what properties to get the values from,
                    //we only have to do this once per type.
                    /*var filterDict = new Dictionary<FilterData, MemberInfo>();
                    foreach( var filter in filters )
                    {
                        MemberInfo info = typeof( T ).GetPropertyOrField( filter.FieldName );
                        if( info == null )
                            throw new Exception( $"ERROR! No property or field found on '{typeof( T ).Name}' by the name '{filter.FieldName}'" );

                        filterDict.Add( filter, info );
                    }

                    Func<T, bool> filterDelegate = ( ( x ) =>
                    {
                        var success = false;

                        foreach( var kvp in filterDict )
                        {
                            object value = ( kvp.Value is PropertyInfo ? ( kvp.Value as PropertyInfo ).GetValue( x ) : ( kvp.Value as FieldInfo ).GetValue( x ) );
                            if( value == null )
                                return false;

                            if( kvp.Value.GetUnderlyingType() == typeof( string ) )
                            {
                                var stringValue = ( (string) value ).ToLower();

                                switch( (FilterMatchMode) kvp.Key.MatchMode )
                                {
                                    case FilterMatchMode.Contains:
                                        success = stringValue.Contains( kvp.Key.Value.ToLower() );
                                        break;
                                    case FilterMatchMode.Equals:
                                        success = stringValue.Equals( kvp.Key.Value.ToLower() );
                                        break;
                                    case FilterMatchMode.StartsWith:
                                        success = stringValue.StartsWith( kvp.Key.Value.ToLower() );
                                        break;
                                    case FilterMatchMode.EndsWith:
                                        success = stringValue.EndsWith( kvp.Key.Value.ToLower() );
                                        break;
                                    default:
                                        throw new Exception( $"ERROR! Unknown filter match mode '{kvp.Key.MatchMode}'" );
                                }
                            }
                            else
                            {
                                success = value.Equals( kvp.Key.Value );
                            }
                        }

                        return success;
                    } );*/

                    /*var parameter = Expression.Parameter( typeof( T ) );
                    Expression exp = Expression.Constant( true );
                    foreach( var filter in filters )
                    {
                        var property = Expression.Equal( Expression.Property( parameter, filter.FieldName ), Expression.Constant( filter.Value ) );
                        exp = Expression.And( exp, property );
                    }*/

                    //var exp = Expression.And( Expression.Constant( true )

                    //var propAsObject = Expression.Convert( property, typeof( object ) );

                    List<FilterDefinition<T>> excludeIncludeFilter = new List<FilterDefinition<T>>();
                    if( idList != null && idList.Any() )
                    {
                        MongoDB.Driver.FilterDefinitionBuilder<T> t2 = Builders<T>.Filter;
                        if( includeList )
                        {
                            var filterList = new List<FilterDefinition<T>>();
                            foreach( var item in idList )
                            {
                                filterList.Add( t2.Eq( "Id", item ) );
                            }
                            excludeIncludeFilter.Add( t2.Or( filterList ) );
                        }
                        else
                        {
                            foreach( var item in idList )
                            {
                                excludeIncludeFilter.Add( t2.Ne( "Id", item ) );
                            }
                        }
                    }

                    IFindFluent<T, T> tes;

                    MongoDB.Driver.FilterDefinitionBuilder<T> t = Builders<T>.Filter;
                    var k = Builders<MongoDB.Bson.BsonDocument>.Filter;
                    List<FilterDefinition<T>> te = new List<FilterDefinition<T>>();
                    te.Add( t.And( t.Ne( "Deleted", true ) ) );

                    /*var query2 = ( rep as MongoRepository<T> ).collection
                        .Aggregate();*/
                    //.Lookup( "Supplier", "Supplier._id", "_id", "Supplier.Resolved" );

                    if( filters != null && filters.Count > 0 )
                    {
                        foreach( var filter in filters )
                        {
                            if( typeof( T ).GetPropertyOrField( filter.FieldName ).GetUnderlyingType() == typeof( DBRef ) )
                            {
                                //if( filter.FieldName == "Supplier" )
                                //    query2 = query2.Match( k.Regex( "Supplier.Resolved.CompanyName", new MongoDB.Bson.BsonRegularExpression( $"{filter.Value}", "i" ) ) );
                                /*else if( filter.FieldName == "Category" )
                                    query2 = query2.Match( k.And( k.Regex( filter.FieldName, new MongoDB.Bson.BsonRegularExpression( $"{filter.Value}", "i" ) ) ) );*/
                                //te.Add( new MongoDB.Bson.BsonDocument( "$where", new MongoDB.Bson.BsonJavaScript( "function() { for( var field in this ) { if( this[field] == \"" + filter.Value + "\") return true; } return false; }" ) ) );
                                continue;
                            }

                            switch( filter.MatchMode )
                            {
                                case FilterMatchMode.Contains:
                                    te.Add( t.Regex( filter.FieldName, new MongoDB.Bson.BsonRegularExpression( $"{filter.Value}", "i" ) ) );
                                    break;
                                case FilterMatchMode.Equals:
                                    te.Add( t.Eq( filter.FieldName, filter.Value ) );
                                    break;
                                case FilterMatchMode.StartsWith:
                                    te.Add( t.Regex( filter.FieldName, new MongoDB.Bson.BsonRegularExpression( $"^{filter.Value}", "i" ) ) );
                                    break;
                                case FilterMatchMode.EndsWith:
                                    te.Add( t.Regex( filter.FieldName, new MongoDB.Bson.BsonRegularExpression( $"{filter.Value}$", "i" ) ) );
                                    break;
                                default:
                                    break;
                            }
                        }
                    }

                    tes = ( rep as MongoRepository<T> ).collection.Find( excludeIncludeFilter != null && excludeIncludeFilter.Count > 0 ? t.And( te.Concat( excludeIncludeFilter ) ) : t.And( te ) );

                    if( typeof( T ) == typeof( Campaign ) || typeof( T ).IsAssignableToGenericType( typeof( DiscountBase ) ) )
                        tes = tes.Project<T>( Builders<T>.Projection.Exclude( "Products" ) );
                    else if( typeof( T ) == typeof( PriceSchedule ) )
                        tes = tes.Project<T>( Builders<T>.Projection.Exclude( "Items" ) );



                    /*query2 = query2
                        .Match( excludedFilters != null && excludedFilters.Count > 0 ? t.And( te.Concat( excludedFilters ) ) : t.And( te ) )
                        .Skip( first )
                        .Limit( count );
                    //.Lookup( "Category", "Category.Id", "Id", "Category.Resolved" )

                    var result = ( await query2.ToListAsync() );*/
                    //var result = ( await query2.ToListAsync() ).Select( x => MongoDB.Bson.Serialization.BsonSerializer.Deserialize<T>( x ) ).ToList();

                    //var totalRecords = 0;
                    //else
                    //    tes = ( rep as MongoRepository<T> ).collection.Find( excludedFilters != null && excludedFilters.Count > 0 ? Builders<T>.Filter.And( excludedFilters ) : FilterDefinition<T>.Empty );

                    /*var totalRecords = await query.CountAsync();
                    var result = await query.Skip( first ).Take( count ).Where( x => teste.Inject() ).ToListAsync();*/

                    //var result = await query.OfType<Product>().WhereAsync( x => x.TitlePOS == "45" ).;

                    //REAL:
                    var totalRecords = await tes.CountAsync();

                    if( first > 0 )
                        tes = tes.Skip( first );
                    if( count > 0 )
                        tes = tes.Limit( count );

                    SortDefinitionBuilder<T> sortBuilder = Builders<T>.Sort;
                    if( sortOrderAscending )
                        tes = tes.Sort( sortBuilder.Ascending( sortField ) );
                    else
                        tes = tes.Sort( sortBuilder.Descending( sortField ) );

                    var result = await tes.ToListAsync();

                    //var result = await query.Where( x => tes.Filter.Inject() ).ToListAsync();

                    //result = result.Except()

                    /*Expression.Lambda<Func<T, bool>>( Expression.Call( Expression.Constant( filterDelegate.Target ), filterDelegate.GetMethodInfo(), Expression.Constant( filterDelegate.Target ) ) )*/

                    /*foreach( var prop in typeof( T ).GetProperties( BindingFlags.Instance | BindingFlags.Public ).Where( x => x.PropertyType == typeof( DBRef ) ) )
                    {
                        foreach( var item in result )
                        {
                            if( prop.Name == "Category" )
                                await ( prop.GetValue( item ) as DBRef ).ResolveAsync<ProductGroup>( database );
                            else if( prop.Name == "Supplier" )
                                await ( prop.GetValue( item ) as DBRef ).ResolveAsync<Supplier>( database );
                        }
                    }*/

                    return new GetStateResult<T>() { Result = result, FilterCount = (int) totalRecords };
                }
                /*else
                {
                    var filterCount = await query.CountAsync();

                    if( first > 0 )
                        query = query.Skip( first );
                    if( count > 0 )
                        query = query.Take( count );

                    var result = await query.ToListAsync();

                    return new GetStateResult<T>() { Result = result, FilterCount = filterCount };
                }*/
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, "ERROR! An exception occured while attempting to get state from database" );
                throw new Exception( "ERROR! An internal server error occured" );
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.GetObjectState() finished for type '{ typeof( T ).Name }'" );
            }
        }

        #region ProductBrand

        [WampProcedure( "erp.inventory.productbrand.create" )]
        public async Task<string> ProductBrandCreate( ProductBrand brand )
        {
            try
            {
                log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.ProductBrandCreate() entered" );

                if( brand == null )
                    throw new WampException( "erp.inventory.productbrand.error.invalidobject", "ERROR! The ProductBrand object cannot be null" );
                if( !string.IsNullOrEmpty( brand.Id ) )
                    throw new WampException( "erp.inventory.productbrand.error.invalidid", $"ERROR! Cannot add a new ProductBrand with an already assigned id" );

                var rep = Database.Get<ProductBrand>();

                await rep.InsertAsync( brand );

                OnProductBrandAdded.OnNext( brand );

                return brand.Id;
            }
            catch( WampException )
            {
                throw;
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, $"{this}.ProductBrandCreate(): failed to insert brand into database" );
                throw new WampException( "erp.error.internalservererror", "ERROR! An internal server error occured" );
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.ProductBrandCreate() finished" );
            }
        }

        [WampProcedure( "erp.inventory.productbrand.getstate" )]
        public async Task<GetStateResult<ProductBrand>> ProductBrandGetState( int first = 0, int count = 0, bool sortOrderAscending = true, string sortField = "Id", List<FilterData> filters = null, List<string> idList = null, bool includeList = true )
        {
            try
            {
                log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.ProductBrandGetState() entered" );

                return await GetObjectState<ProductBrand>( first, count, sortOrderAscending, sortField, filters, idList: idList, includeList: includeList );
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, $"{this}.ProductBrandGetState(): failed to get state of productbrands from database" );
                throw new WampException( "erp.error.internalservererror", "ERROR! An internal server error occured" );
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.ProductBrandGetState() finished" );
            }
        }

        [WampProcedure( "erp.inventory.productbrand.update" )]
        public async Task ProductBrandUpdate( ProductBrand brandModifications, UpdateAction[] actions = null )
        {
            try
            {
                log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.ProductBrandUpdate() entered" );

                if( brandModifications == null )
                    throw new WampException( "erp.inventory.productbrand.error.invalidobject", "ERROR! ProductBrand cannot be null" );
                if( string.IsNullOrEmpty( brandModifications.Id ) )
                    throw new WampException( "erp.inventory.productbrand.error.invalidid", "ERROR! Id of ProductBrand cannot be null or empty" );

                var repo = await Database.GetAsync<ProductBrand>();

                var found = await repo.Query.FirstOrDefaultAsync( x => x.Id == brandModifications.Id );
                if( found == null )
                    throw new WampException( "erp.inventory.productbrand.error.invalidid", $"ERROR! Failed to find productbrand with given id '{brandModifications.Id}'" );

                await repo.UpdateAsync( brandModifications, actions );

                found = await repo.Query.FirstOrDefaultAsync( x => x.Id == brandModifications.Id );

                OnProductBrandUpdated.OnNext( found );
            }
            catch( WampException )
            {
                throw;
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, $"{this}.ProductBrandUpdate(): failed to update productbrand in database" );
                throw new WampException( "erp.error.internalservererror", "ERROR! An internal server error occured" );
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.ProductBrandUpdate() finished" );
            }
        }

        [WampProcedure( "erp.inventory.productbrand.replace" )]
        public async Task ProductBrandReplace( ProductBrand brand )
        {
            try
            {
                log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.ProductBrandReplace() entered" );

                if( brand == null )
                    throw new WampException( "erp.inventory.productbrand.error.invalidobject", "ERROR! ProductBrand cannot be null" );
                if( string.IsNullOrEmpty( brand.Id ) )
                    throw new WampException( "erp.inventory.productbrand.error.invalidid", "ERROR! Id of ProductBrand cannot be null or empty" );

                var rep = Database.Get<ProductBrand>();

                log.LogTrace( Event.VARIABLEGET, $"{this}.ProductBrandReplace(): looking for existing productbrand with id: '{brand.Id}'" );
                var found = await rep.Query.FirstOrDefaultAsync( x => x.Id == brand.Id );
                if( found == null )
                    throw new WampException( "erp.inventory.productbrand.error.invalidid", $"ERROR! No productbrand with the id: '{ brand.Id }'" );

                await rep.ReplaceAsync( brand );

                OnProductBrandUpdated.OnNext( brand );
            }
            catch( WampException )
            {
                throw;
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, $"{this}.ProductBrandReplace(): failed to replace productbrand in database" );
                throw new WampException( "erp.error.internalservererror", "ERROR! An internal server error occured" );
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.ProductBrandReplace() finished" );
            }
        }

        [WampProcedure( "erp.inventory.productbrand.delete" )]
        public async Task ProductBrandDelete( string id )
        {
            try
            {
                log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.ProductBrandDelete() entered" );

                if( string.IsNullOrEmpty( id ) )
                    throw new WampException( "erp.inventory.productbrand.error.invalidid", "id" );

                var rep = Database.Get<ProductBrand>();

                log.LogTrace( Event.VARIABLEGET, $"{this}.ProductBrandDelete(): looking for existing productbrand with id: '{id}'" );
                var found = await rep.Query.FirstOrDefaultAsync( x => x.Id == id );
                if( found == null )
                    throw new WampException( "erp.inventory.productbrand.error.invalidid", $"ERROR! No productbrand with the id: '{ id }' found" );

                await rep.DeleteAsync( id );

                OnProductBrandDeleted.OnNext( id );
            }
            catch( WampException )
            {
                throw;
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, $"{this}.ProductBrandDelete(): failed to delete productbrand in database" );
                throw new WampException( "erp.error.internalservererror", "ERROR! An internal server error occured" );
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.ProductBrandDelete() finished" );
            }
        }

        #endregion

        #region Supplier
        [WampProcedure( "erp.inventory.supplier.create" )]
        public async Task<string> SupplierCreate( Supplier supplier )
        {
            log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.SupplierCreate() entered" );

            if( supplier == null )
                throw new ArgumentNullException( "supplier" );
            if( !string.IsNullOrEmpty( supplier.Id ) )
                throw new WampException( new Dictionary<string, object>(), "erp.crm.supplier.error.invalidid", $"ERROR! Cannot add a new supplier with an already assigned id", null );

            var rep = Database.Get<Supplier>();

            try
            {
                await rep.InsertAsync( supplier );

                OnSupplierAdded.OnNext( supplier );
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, "ERROR! Failed to insert supplier into database" );
                throw;
            }

            log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.SupplierCreate() finished" );

            return supplier.Id;
        }

        [WampProcedure( "erp.inventory.supplier.getstate" )]
        public async Task<GetStateResult<Supplier>> SupplierGetState( int first = 0, int count = 0, bool sortOrderAscending = true, string sortField = "Id", List<FilterData> filters = null, List<string> idList = null, bool includeList = true )
        {
            log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.SupplierGetState() entered" );

            try
            {
                return await GetObjectState<Supplier>( first, count, sortOrderAscending, sortField, filters, idList: idList, includeList: includeList );
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, "ERROR! Failed to get state of supplier from database" );
                throw new Exception( "ERROR! An internal server error occured" );
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.SupplierGetState() finished" );
            }
        }

        [WampProcedure( "erp.inventory.supplier.update" )]
        public async Task SupplierUpdate( Supplier supplierModifications, UpdateAction[] actions = null )
        {
            try
            {
                log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.SupplierUpdate() entered" );

                if( supplierModifications == null )
                    throw new WampException( "erp.inventory.supplier.error.invalidobject", "ERROR! Supplier cannot be null" );
                if( string.IsNullOrEmpty( supplierModifications.Id ) )
                    throw new WampException( "erp.inventory.supplier.error.invalidid", "ERROR! Id of Supplier cannot be null or empty" );

                var repo = await Database.GetAsync<Supplier>();

                var found = await repo.Query.FirstOrDefaultAsync( x => x.Id == supplierModifications.Id );
                if( found == null )
                    throw new WampException( "erp.inventory.supplier.error.invalidid", $"ERROR! Failed to find supplier with given id '{supplierModifications.Id}'" );

                await repo.UpdateAsync( supplierModifications, actions );

                found = await repo.Query.FirstOrDefaultAsync( x => x.Id == supplierModifications.Id );

                OnSupplierUpdated.OnNext( found );
            }
            catch( WampException )
            {
                throw;
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, $"{this}.SupplierUpdate(): failed to update supllier in database" );
                throw new WampException( "erp.error.internalservererror", "ERROR! An internal server error occured" );
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.SupplierUpdate() finished" );
            }
        }

        [WampProcedure( "erp.inventory.supplier.replace" )]
        public async Task SupplierReplace( Supplier supplier )
        {
            try
            {
                log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.SupplierReplace() entered" );

                if( supplier == null )
                    throw new WampException( "erp.inventory.supplier.error.invalidobject", "ERROR! Supplier cannot be null" );
                if( string.IsNullOrEmpty( supplier.Id ) )
                    throw new WampException( "erp.inventory.supplier.error.invalidid", "ERROR! Id of Supplier cannot be null or empty" );

                var rep = Database.Get<Supplier>();

                log.LogTrace( Event.VARIABLEGET, $"{this}.SupplierReplace(): looking for existing supplier with id: '{supplier.Id}'" );
                var found = await rep.Query.FirstOrDefaultAsync( x => x.Id == supplier.Id );
                if( found == null )
                    throw new WampException( "erp.inventory.supplier.error.invalidid", $"ERROR! No supplier with the id: '{ supplier.Id }'" );

                await rep.ReplaceAsync( supplier );

                OnSupplierUpdated.OnNext( supplier );
            }
            catch( WampException )
            {
                throw;
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, $"{this}.SupplierReplace(): failed to replace supplier in database" );
                throw new WampException( "erp.error.internalservererror", "ERROR! An internal server error occured" );
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.SupplierReplace() finished" );
            }
        }

        [WampProcedure( "erp.inventory.supplier.delete" )]
        public async Task SupplierDelete( string id )
        {
            log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.SupplierDelete() entered" );

            if( string.IsNullOrEmpty( id ) )
                throw new ArgumentNullException( "id" );

            var rep = Database.Get<Supplier>();

            log.LogTrace( Event.VARIABLEGET, $"{this}.SupplierDelete(): looking for existing supplier with id: '{id}'" );
            var foundAssortment = await rep.Query.FirstOrDefaultAsync( x => x.Id == id );
            if( foundAssortment == null )
                throw new WampException( new Dictionary<string, object>(), "erp.inventory.supplier.error.invalidid", $"ERROR! No supplier with the id: '{ id }'", null );

            try
            {
                await rep.DeleteAsync( id );

                OnSupplierDeleted.OnNext( id );
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, "ERROR! Failed to delete supplier in database" );
                throw;
            }

            log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.SupplierDelete() finished" );
        }

        #endregion

        #region Unit
        [WampProcedure( "erp.unit.create" )]
        public async Task<string> UnitCreate( Unit unit )
        {
            log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.UnitCreate() entered" );

            if( unit == null )
                throw new ArgumentNullException( "unit" );
            if( !string.IsNullOrEmpty( unit.Id ) )
                throw new WampException( new Dictionary<string, object>(), "erp.crm.unit.error.invalidid", $"ERROR! Cannot add a new unit with an already assigned id", null );

            var rep = Database.Get<Unit>();

            try
            {
                await rep.InsertAsync( unit );

                OnUnitAdded.OnNext( unit );
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, "ERROR! Failed to insert unit into database" );
                throw;
            }


            log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.UnitCreate() finished" );

            return unit.Id;
        }

        [WampProcedure( "erp.unit.getstate" )]
        public async Task<GetStateResult<Unit>> UnitGetState( int first = 0, int count = 50, bool sortOrderAscending = true, string sortField = "Id", List<FilterData> filters = null )
        {
            log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.UnitGetState() entered" );

            try
            {
                return await GetObjectState<Unit>( first, count, sortOrderAscending, sortField, filters );
            }
            catch( Exception )
            {
                throw;
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.UnitGetState() finished" );
            }
        }

        [WampProcedure( "erp.unit.update" )]
        public async Task UnitUpdate( Unit unitModifications, UpdateAction[] actions = null )
        {
            try
            {
                log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.UnitUpdate() entered" );

                if( unitModifications == null )
                    throw new WampException( "erp.unit.error.invalidobject", "ERROR! Unit cannot be null" );
                if( string.IsNullOrEmpty( unitModifications.Id ) )
                    throw new WampException( "erp.unit.error.invalidid", "ERROR! Id of Unit cannot be null or empty" );

                var repo = await Database.GetAsync<Unit>();

                var found = await repo.Query.FirstOrDefaultAsync( x => x.Id == unitModifications.Id );
                if( found == null )
                    throw new WampException( "erp.unit.error.invalidid", $"ERROR! Failed to find unit with given id '{unitModifications.Id}'" );

                await repo.UpdateAsync( unitModifications, actions );

                found = await repo.Query.FirstOrDefaultAsync( x => x.Id == unitModifications.Id );

                OnUnitUpdated.OnNext( found );
            }
            catch( WampException )
            {
                throw;
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, $"{this}.UnitUpdate(): failed to update unit in database" );
                throw new WampException( "erp.error.internalservererror", "ERROR! An internal server error occured" );
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.UnitUpdate() finished" );
            }
        }

        [WampProcedure( "erp.unit.replace" )]
        public async Task UnitReplace( Unit unit )
        {
            try
            {
                log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.UnitReplace() entered" );

                if( unit == null )
                    throw new WampException( "erp.unit.error.invalidobject", "ERROR! Unit cannot be null" );
                if( string.IsNullOrEmpty( unit.Id ) )
                    throw new WampException( "erp.unit.error.invalidid", "ERROR! Id of Ünit cannot be null or empty" );

                var rep = Database.Get<Unit>();

                log.LogTrace( Event.VARIABLEGET, $"{this}.UnitReplace(): looking for existing unit with id: '{unit.Id}'" );
                var found = await rep.Query.FirstOrDefaultAsync( x => x.Id == unit.Id );
                if( found == null )
                    throw new WampException( "erp.unit.error.invalidid", $"ERROR! No supplier with the id: '{ unit.Id }'" );

                await rep.ReplaceAsync( unit );

                OnUnitUpdated.OnNext( unit );
            }
            catch( WampException )
            {
                throw;
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, $"{this}.unitReplace(): failed to replace unit in database" );
                throw new WampException( "erp.error.internalservererror", "ERROR! An internal server error occured" );
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.UnitReplace() finished" );
            }
        }

        #endregion

        #region Product

        [WampProcedure( "erp.inventory.product.create" )]
        public async Task<string> ProductCreate( Product product )
        {
            log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.ProductCreate() entered" );

            if( product == null )
                throw new ArgumentNullException( "product" );
            if( !string.IsNullOrEmpty( product.Id ) )
                throw new WampException( new Dictionary<string, object>(), "erp.crm.product.error.invalidid", $"ERROR! Cannot add a new product with an already assigned id", null );

            var rep = Database.Get<Product>();

            try
            {
                await rep.InsertAsync( product );

                OnProductAdded.OnNext( product );
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, "ERROR! Failed to insert product into database" );
                throw;
            }

            log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.ProductCreate() finished" );

            return product.Id;
        }

        [WampProcedure( "erp.inventory.product.getstate" )]
        public async Task<GetStateResult<Product>> ProductGetState( int first = 0, int count = 50, bool sortOrderAscending = true, string sortField = "Id", List<FilterData> filters = null, List<string> idList = null, bool includeList = true )
        {
            log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.ProductGetState() entered" );

            try
            {
                return await GetObjectState<Product>( first, count, sortOrderAscending, sortField, filters, includeList: includeList, idList: idList );
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, "ERROR! Failed to get state of product from database" );
                throw new Exception( "ERROR! An internal server error occured" );
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.ProductGetState() finished" );
            }
        }

        [WampProcedure( "erp.inventory.product.update" )]
        public async Task ProductUpdate( Product productModifications, UpdateAction[] actions = null )
        {
            try
            {
                log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.ProductUpdate() entered" );

                if( productModifications == null )
                    throw new WampException( "erp.inventory.product.error.invalidobject", "ERROR! Product cannot be null" );
                if( string.IsNullOrEmpty( productModifications.Id ) )
                    throw new WampException( "erp.inventory.product.error.invalidid", "ERROR! Id of Product cannot be null or empty" );

                var repo = await Database.GetAsync<Product>();

                var found = await repo.Query.FirstOrDefaultAsync( x => x.Id == productModifications.Id );
                if( found == null )
                    throw new WampException( "erp.inventory.product.error.invalidid", $"ERROR! Failed to find product with given id '{productModifications.Id}'" );

                await repo.UpdateAsync( productModifications, actions );

                found = await repo.Query.FirstOrDefaultAsync( x => x.Id == productModifications.Id );

                OnProductUpdated.OnNext( found );
            }
            catch( WampException )
            {
                throw;
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, $"{this}.ProductUpdate(): failed to update product in database" );
                throw new WampException( "erp.error.internalservererror", "ERROR! An internal server error occured" );
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.ProductUpdate() finished" );
            }
        }

        [WampProcedure( "erp.inventory.product.replace" )]
        public async Task ProductReplace( Product product )
        {
            try
            {
                log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.ProductReplace() entered" );

                if( product == null )
                    throw new WampException( "erp.inventory.product.error.invalidobject", "ERROR! Product cannot be null" );
                if( string.IsNullOrEmpty( product.Id ) )
                    throw new WampException( "erp.inventory.product.error.invalidid", "ERROR! Id of Product cannot be null or empty" );

                var rep = Database.Get<Product>();

                log.LogTrace( Event.VARIABLEGET, $"{this}.ProductReplace(): looking for existing product with id: '{product.Id}'" );
                var found = await rep.Query.FirstOrDefaultAsync( x => x.Id == product.Id );
                if( found == null )
                    throw new WampException( "erp.inventory.product.error.invalidid", $"ERROR! No product with the id: '{ product.Id }'" );

                await rep.ReplaceAsync( product );

                OnProductUpdated.OnNext( product );
            }
            catch( WampException )
            {
                throw;
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, $"{this}.ProductReplace(): failed to replace product in database" );
                throw new WampException( "erp.error.internalservererror", "ERROR! An internal server error occured" );
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.ProductReplace() finished" );
            }
        }

        [WampProcedure( "erp.inventory.product.delete" )]
        public async Task ProductDelete( string id )
        {
            log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.ProductDelete() entered" );

            if( string.IsNullOrEmpty( id ) )
                throw new ArgumentNullException( "id" );

            var rep = Database.Get<Product>();

            log.LogTrace( Event.VARIABLEGET, $"{this}.ProductDelete(): looking for existing product with id: '{id}'" );
            var found = await rep.Query.FirstOrDefaultAsync( x => x.Id == id );
            if( found == null )
                throw new WampException( new Dictionary<string, object>(), "erp.inventory.product.error.invalidid", $"ERROR! No product with the id: '{ id }'", null );

            try
            {
                await rep.DeleteAsync( id );

                OnProductDeleted.OnNext( id );
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, "ERROR! Failed to delete product in database" );
                throw;
            }

            log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.ProductDelete() finished" );
        }

        [WampProcedure( "erp.inventory.product.getcampaigns" )]
        public async Task<Campaign[]> ProductGetCampaigns( string id )
        {
            log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.ProductGetCampaigns() entered" );

            if( string.IsNullOrEmpty( id ) )
                throw new ArgumentNullException( "id" );

            try
            {
                var rep = Database.Get<Campaign>();

                //TODO: Don't cast to IMongoQueryable
                var query = ( rep.Query as IMongoQueryable<Campaign> ).Where( x => x.Products.Any( y => y.Product == new DBRef( id, "", "" ) || y.Product == new DBRef( id ) ) );
                //var test = query.ToString();
                var result = await query.ToListAsync();
                return result.ToArray();
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, "ERROR! " );
                throw new Exception( "ERROR! An internal server error occured" );
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.ProductGetCampaigns() finished" );
            }
        }

        #endregion

        #region ProductGroup

        [WampProcedure( "erp.inventory.productgroup.create" )]
        public async Task<string> ProductGroupCreate( ProductGroup productGroup )
        {
            log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.ProductGroupCreate() entered" );

            if( productGroup == null )
                throw new ArgumentNullException( "productgroup" );
            if( !string.IsNullOrEmpty( productGroup.Id ) )
                throw new Exception( $"ERROR! Cannot add a new productgroup with an already assigned id" );

            var rep = Database.Get<ProductGroup>();

            try
            {
                await rep.InsertAsync( productGroup );

                OnProductGroupAdded.OnNext( productGroup );
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, "ERROR! Failed to insert productgroup into database" );
                throw;
            }

            log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.ProductGroupCreate() finished" );

            return productGroup.Id;
        }

        [WampProcedure( "erp.inventory.productgroup.getstate" )]
        public async Task<GetStateResult<ProductGroup>> ProductGroupGetState( int first = 0, int count = 50, bool sortOrderAscending = true, string sortField = "Id", List<FilterData> filters = null, List<string> idList = null, bool includeList = true )
        {
            try
            {
                return await GetObjectState<ProductGroup>( first, count, sortOrderAscending, sortField, filters, includeList: includeList, idList: idList );
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, "ERROR! Failed to get state of product group from database" );
                throw new Exception( "ERROR! An internal server error occured" );
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.ProductGroupGetState() finished" );
            }
        }

        [WampProcedure( "erp.inventory.productgroup.update" )]
        public async Task ProductGroupUpdate( ProductGroup groupModifications, UpdateAction[] actions = null )
        {
            try
            {
                log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.ProductGroupUpdate() entered" );

                if( groupModifications == null )
                    throw new WampException( "erp.inventory.productgroup.error.invalidobject", "ERROR! ProductGroup cannot be null" );
                if( string.IsNullOrEmpty( groupModifications.Id ) )
                    throw new WampException( "erp.inventory.productgroup.error.invalidid", "ERROR! Id of ProductGroup cannot be null or empty" );

                var repo = await Database.GetAsync<ProductGroup>();

                var found = await repo.Query.FirstOrDefaultAsync( x => x.Id == groupModifications.Id );
                if( found == null )
                    throw new WampException( "erp.inventory.productgroup.error.invalidid", $"ERROR! Failed to find productgroup with given id '{groupModifications.Id}'" );

                await repo.UpdateAsync( groupModifications, actions );

                found = await repo.Query.FirstOrDefaultAsync( x => x.Id == groupModifications.Id );

                OnProductGroupUpdated.OnNext( found );
            }
            catch( WampException )
            {
                throw;
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, $"{this}.ProductGroupUpdate(): failed to update ProductGroup in database" );
                throw new WampException( "erp.error.internalservererror", "ERROR! An internal server error occured" );
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.ProductGroupUpdate() finished" );
            }
        }

        [WampProcedure( "erp.inventory.productgroup.replace" )]
        public async Task ProductGroupReplace( ProductGroup group )
        {
            try
            {
                log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.ProductGroupReplace() entered" );

                if( group == null )
                    throw new WampException( "erp.inventory.productgroup.error.invalidobject", "ERROR! ProductGroup cannot be null" );
                if( string.IsNullOrEmpty( group.Id ) )
                    throw new WampException( "erp.inventory.productgroup.error.invalidid", "ERROR! Id of ProductGroup cannot be null or empty" );

                var rep = Database.Get<ProductGroup>();

                log.LogTrace( Event.VARIABLEGET, $"{this}.ProductGroupReplace(): looking for existing productgroup with id: '{group.Id}'" );
                var found = await rep.Query.FirstOrDefaultAsync( x => x.Id == group.Id );
                if( found == null )
                    throw new WampException( "erp.inventory.productgroup.error.invalidid", $"ERROR! No productgroup with the id: '{ group.Id }'" );

                await rep.ReplaceAsync( group );

                OnProductGroupUpdated.OnNext( group );
            }
            catch( WampException )
            {
                throw;
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, $"{this}.ProductGroupReplace(): failed to replace productgroup in database" );
                throw new WampException( "erp.error.internalservererror", "ERROR! An internal server error occured" );
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.ProductGroupReplace() finished" );
            }
        }

        [WampProcedure( "erp.inventory.productgroup.delete" )]
        public async Task ProductGroupDelete( string id )
        {
            log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.ProductGroupDelete() entered" );

            if( string.IsNullOrEmpty( id ) )
                throw new ArgumentNullException( "id" );

            var rep = Database.Get<ProductGroup>();

            log.LogTrace( Event.VARIABLEGET, $"{this}.ProductGroupDelete(): looking for existing productgroup with id: '{id}'" );
            var found = await rep.Query.FirstOrDefaultAsync( x => x.Id == id );
            if( found == null )
                throw new Exception( $"ERROR! No productgroup with the id: '{ id }' found" );

            try
            {
                await rep.DeleteAsync( id );

                OnProductGroupDeleted.OnNext( id );
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, "ERROR! Failed to delete productgroup in database" );
                throw;
            }

            log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.ProductGroupDelete() finished" );
        }

        #endregion

        #region ProductSpecification

        [WampProcedure( "erp.inventory.productspecification.create" )]
        public async Task<string> ProductSpecificationCreate( ProductSpecification specification )
        {
            try
            {
                log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.ProductSpecificationCreate() entered" );

                if( specification == null )
                    throw new WampException( "erp.inventory.productspecification.error.invalidobject", "ERROR! The ProductSpecification object cannot be null" );
                if( !string.IsNullOrEmpty( specification.Id ) )
                    throw new WampException( "erp.inventory.productspecification.error.invalidid", $"ERROR! Cannot add a new ProductSpecification with an already assigned id" );

                var rep = Database.Get<ProductSpecification>();

                await rep.InsertAsync( specification );

                OnProductSpecificationAdded.OnNext( specification );

                return specification.Id;
            }
            catch( WampException )
            {
                throw;
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, $"{this}.ProductSpecificationCreate(): failed to insert productspecification into database" );
                throw new WampException( "erp.error.internalservererror", "ERROR! An internal server error occured" );
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.ProductSpecificationCreate() finished" );
            }
        }

        [WampProcedure( "erp.inventory.productspecification.getstate" )]
        public async Task<GetStateResult<ProductSpecification>> ProductSpecificationGetState( int first = 0, int count = 0, bool sortOrderAscending = true, string sortField = "Id", List<FilterData> filters = null, List<string> idList = null, bool includeList = true )
        {
            try
            {
                log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.ProductSpecificationGetState() entered" );

                return await GetObjectState<ProductSpecification>( first, count, sortOrderAscending, sortField, filters, idList: idList, includeList: includeList );
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, $"{this}.ProductSpecificationGetState(): failed to get state of productspecifications from database" );
                throw new WampException( "erp.error.internalservererror", "ERROR! An internal server error occured" );
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.ProductSpecificationGetState() finished" );
            }
        }

        [WampProcedure( "erp.inventory.productspecification.update" )]
        public async Task ProductSpecificationUpdate( ProductSpecification specificationModifications, UpdateAction[] actions = null )
        {
            try
            {
                log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.ProductSpecificationUpdate() entered" );

                if( specificationModifications == null )
                    throw new WampException( "erp.inventory.productspecification.event.invalidobject", "ERROR! ProductSpecification cannot be null" );
                if( string.IsNullOrEmpty( specificationModifications.Id ) )
                    throw new WampException( "erp.inventory.productspecification.event.invalidid", "ERROR! Id of ProductSpecification cannot be null or empty" );

                var repo = await Database.GetAsync<ProductSpecification>();

                var found = await repo.Query.FirstOrDefaultAsync( x => x.Id == specificationModifications.Id );
                if( found == null )
                    throw new WampException( "erp.inventory.productspecification.error.invalidid", $"ERROR! Failed to find productspecification with given id '{specificationModifications.Id}'" );

                await repo.UpdateAsync( specificationModifications, actions );

                found = await repo.Query.FirstOrDefaultAsync( x => x.Id == specificationModifications.Id );

                OnProductSpecificationUpdated.OnNext( found );
            }
            catch( WampException )
            {
                throw;
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, $"{this}.ProductSpecificationUpdate(): failed to update productspecification in database" );
                throw new WampException( "erp.error.internalservererror", "ERROR! An internal server error occured" );
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.ProductSpecificationUpdate() finished" );
            }
        }

        [WampProcedure( "erp.inventory.productspecification.replace" )]
        public async Task ProductSpecificationReplace( ProductSpecification specification )
        {
            try
            {
                log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.ProductSpecificationReplace() entered" );

                if( specification == null )
                    throw new WampException( "erp.inventory.productspecification.event.invalidobject", "ERROR! ProductSpecification cannot be null" );
                if( string.IsNullOrEmpty( specification.Id ) )
                    throw new WampException( "erp.inventory.productspecification.event.invalidid", "ERROR! Id of ProductSpecification cannot be null or empty" );

                var rep = Database.Get<ProductSpecification>();

                log.LogTrace( Event.VARIABLEGET, $"{this}.ProductSpecificationReplace(): looking for existing productspecification with id: '{specification.Id}'" );
                var found = await rep.Query.FirstOrDefaultAsync( x => x.Id == specification.Id );
                if( found == null )
                    throw new WampException( "erp.inventory.productspecification.error.invalidid", $"ERROR! No productspecification with the id: '{ specification.Id }'" );

                await rep.ReplaceAsync( specification );

                OnProductSpecificationUpdated.OnNext( specification );
            }
            catch( WampException )
            {
                throw;
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, $"{this}.ProductSpecificationReplace(): failed to replace productspecification in database" );
                throw new WampException( "erp.error.internalservererror", "ERROR! An internal server error occured" );
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.ProductSpecificationReplace() finished" );
            }
        }

        [WampProcedure( "erp.inventory.productspecification.delete" )]
        public async Task ProductSpecificationDelete( string id )
        {
            try
            {
                log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.ProductSpecificationDelete() entered" );

                if( string.IsNullOrEmpty( id ) )
                    throw new WampException( "erp.inventory.productspecification.event.invalidid", "id" );

                var rep = Database.Get<ProductSpecification>();

                log.LogTrace( Event.VARIABLEGET, $"{this}.ProductSpecificationDelete(): looking for existing productspecification with id: '{id}'" );
                var found = await rep.Query.FirstOrDefaultAsync( x => x.Id == id );
                if( found == null )
                    throw new WampException( "erp.inventory.productspecification.error.invalidid", $"ERROR! No productspecification with the id: '{ id }' found" );

                await rep.DeleteAsync( id );

                OnProductSpecificationDeleted.OnNext( id );
            }
            catch( WampException )
            {
                throw;
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, $"{this}.ProductSpecificationDelete(): failed to delete productspecification in database" );
                throw new WampException( "erp.error.internalservererror", "ERROR! An internal server error occured" );
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.ProductSpecificationDelete() finished" );
            }
        }

        #endregion

        #region ProductSpecificationUnit

        [WampProcedure( "erp.inventory.productspecificationunit.getstate" )]
        public async Task<GetStateResult<ProductSpecificationUnit>> ProductSpecificationUnitGetState( int first = 0, int count = 0, bool sortOrderAscending = true, string sortField = "Id", List<FilterData> filters = null, List<string> idList = null, bool includeList = true )
        {
            try
            {
                log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.ProductSpecificationUnitGetState() entered" );

                return await GetObjectState<ProductSpecificationUnit>( first, count, sortOrderAscending, sortField, filters, idList: idList, includeList: includeList );
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, $"{this}.ProductSpecificationUnitGetState(): failed to get state of productspecificationunits from database" );
                throw new WampException( "erp.error.internalservererror", "ERROR! An internal server error occured" );
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.ProductSpecificationUnitGetState() finished" );
            }
        }

        #endregion

        #region Assortment

        [WampProcedure( "erp.inventory.assortment.create" )]
        public async Task<Assortment> AssortmentCreate( Assortment assortment )
        {
            log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.CreateAssortment() entered" );

            if( assortment == null )
                throw new ArgumentNullException( "assortment" );
            if( !string.IsNullOrEmpty( assortment.Id ) )
                throw new WampException( new Dictionary<string, object>(), "erp.inventory.assortment.error.invalidid", $"ERROR! Cannot add a new assortment with an already assigned id", null );

            var rep = Database.Get<Assortment>();

            try
            {
                await rep.InsertAsync( assortment );

                OnAssortmentAdded.OnNext( assortment );
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, "ERROR! Failed to insert assortment into database" );
                throw;
            }

            log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.CreateAssortment() finished" );

            return assortment;
        }

        [WampProcedure( "erp.inventory.assortment.getstate" )]
        public async Task<GetStateResult<Assortment>> AssortmentGetState( int first = 0, int count = 50, bool sortOrderAscending = true, string sortField = "Id", List<FilterData> filters = null )
        {
            log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.AssortmentGetState() entered" );

            try
            {
                return await GetObjectState<Assortment>( first, count, sortOrderAscending, sortField, filters );
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, "ERROR! Failed to get state of assortment from database" );
                throw new Exception( "ERROR! An internal server error occured" );
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.AssortmentGetState() finished" );
            }
        }

        [WampProcedure( "erp.inventory.assortment.get" )]
        public async Task<string> AssortmentGet( string id )
        {
            log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.GetAssortment() entered" );

            var rep = Database.Get<Assortment>();

            log.LogInformation( $"Executing assortment.get with {id}" );
            var foundAssortment = await rep.Query.FirstOrDefaultAsync( x => x.Id == id );
            if( foundAssortment == null )
                throw new WampException( new Dictionary<string, object>(), "erp.inventory.assortment.error.invalidid", $"ERROR! No assortment with the id {id}", null );

            log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.GetAssortment() finished" );

            return foundAssortment.Id;
        }

        [WampProcedure( "erp.inventory.assortment.update" )]
        public async Task AssortmentUpdate( Assortment assortmentModifications, UpdateAction[] actions = null )
        {
            try
            {
                log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.AssortmentUpdate() entered" );

                if( assortmentModifications == null )
                    throw new WampException( "erp.inventory.assortment.error.invalidobject", "ERROR! Assortment cannot be null" );
                if( string.IsNullOrEmpty( assortmentModifications.Id ) )
                    throw new WampException( "erp.inventory.assortment.error.invalidid", "ERROR! Id of Assortment cannot be null or empty" );

                var repo = await Database.GetAsync<Assortment>();

                var found = await repo.Query.FirstOrDefaultAsync( x => x.Id == assortmentModifications.Id );
                if( found == null )
                    throw new WampException( "erp.inventory.assortment.error.invalidid", $"ERROR! Failed to find assortment with given id '{assortmentModifications.Id}'" );

                await repo.UpdateAsync( assortmentModifications, actions );

                found = await repo.Query.FirstOrDefaultAsync( x => x.Id == assortmentModifications.Id );

                OnAssortmentUpdated.OnNext( found );
            }
            catch( WampException )
            {
                throw;
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, $"{this}.AssortmentUpdate(): failed to update assortment in database" );
                throw new WampException( "erp.error.internalservererror", "ERROR! An internal server error occured" );
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.AssortmentUpdate() finished" );
            }
        }

        [WampProcedure( "erp.inventory.assortment.replace" )]
        public async Task AssortmentReplace( Assortment assortment )
        {
            try
            {
                log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.AssortmentReplace() entered" );

                if( assortment == null )
                    throw new WampException( "erp.inventory.assortment.error.invalidobject", "ERROR! Assortment cannot be null" );
                if( string.IsNullOrEmpty( assortment.Id ) )
                    throw new WampException( "erp.inventory.assortment.error.invalidid", "ERROR! Id of Assortment cannot be null or empty" );

                var rep = Database.Get<Assortment>();

                log.LogTrace( Event.VARIABLEGET, $"{this}.AssortmentReplace(): looking for existing assortment with id: '{assortment.Id}'" );
                var found = await rep.Query.FirstOrDefaultAsync( x => x.Id == assortment.Id );
                if( found == null )
                    throw new WampException( "erp.inventory.assortment.error.invalidid", $"ERROR! No assortment with the id: '{ assortment.Id }'" );

                await rep.ReplaceAsync( assortment );

                OnAssortmentUpdated.OnNext( assortment );
            }
            catch( WampException )
            {
                throw;
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, $"{this}.AssortmentReplace(): failed to replace assortment in database" );
                throw new WampException( "erp.error.internalservererror", "ERROR! An internal server error occured" );
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.AssortmentReplace() finished" );
            }
        }

        [WampProcedure( "erp.inventory.assortment.delete" )]
        public async Task AssortmentDelete( string id )
        {
            log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.RemoveAssortment() entered" );

            if( string.IsNullOrEmpty( id ) )
                throw new ArgumentNullException( "id" );

            var rep = Database.Get<Assortment>();

            log.LogTrace( Event.VARIABLEGET, $"{this}.RemoveAssortment(): looking for existing assortment with id: '{id}'" );
            var foundAssortment = await rep.Query.FirstOrDefaultAsync( x => x.Id == id );
            if( foundAssortment == null )
                throw new WampException( new Dictionary<string, object>(), "erp.inventory.assortment.error.invalidid", $"ERROR! No assortment with the id: '{ id }'", null );

            try
            {
                await rep.DeleteAsync( id );

                OnAssortmentDeleted.OnNext( id );
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, "ERROR! Failed to delete assortment in database" );
                throw;
            }

            log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.RemoveAssortment() finished" );
        }

        #endregion

        #region Customer

        [WampProcedure( "erp.crm.customer.create" )]
        public async Task<string> CustomerCreate( Customer customer )
        {
            log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.CustomerCreate() entered" );

            if( customer == null )
                throw new ArgumentNullException( "customer" );
            if( !string.IsNullOrEmpty( customer.Id ) )
                throw new WampException( new Dictionary<string, object>(), "erp.crm.customer.error.invalidid", $"ERROR! Cannot add a new customer with an already assigned id", null );

            var rep = Database.Get<Customer>();

            try
            {
                await rep.InsertAsync( customer );

                OnCustomerAdded.OnNext( customer );
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, "ERROR! Failed to insert customer into database" );
                throw;
            }

            log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.CustomerCreate() finished" );

            return customer.Id;
        }

        [WampProcedure( "erp.crm.customer.getstate" )]
        public async Task<GetStateResult<Customer>> CustomerGetState( int first = 0, int count = 50, bool sortOrderAscending = true, string sortField = "Id", List<FilterData> filters = null )
        {
            log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.CustomerGetState() entered" );

            try
            {
                return await GetObjectState<Customer>( first, count, sortOrderAscending, sortField, filters );
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, "ERROR! Failed to get state of customer from database" );
                throw new Exception( "ERROR! An internal server error occured" );
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.CustomerGetState() finished" );
            }
        }

        [WampProcedure( "erp.crm.customer.update" )]
        public async Task CustomerUpdate( Customer customerModifications, UpdateAction[] actions = null )
        {
            try
            {
                log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.CustomerUpdate() entered" );

                if( customerModifications == null )
                    throw new WampException( "erp.crm.customer.error.invalidobject", "ERROR! Customer cannot be null" );
                if( string.IsNullOrEmpty( customerModifications.Id ) )
                    throw new WampException( "erp.crm.customer.error.invalidid", "ERROR! Id of Customer cannot be null or empty" );

                var repo = await Database.GetAsync<Customer>();

                var found = await repo.Query.FirstOrDefaultAsync( x => x.Id == customerModifications.Id );
                if( found == null )
                    throw new WampException( "erp.crm.customer.error.invalidid", $"ERROR! Failed to find customer with given id '{customerModifications.Id}'" );

                await repo.UpdateAsync( customerModifications, actions );

                found = await repo.Query.FirstOrDefaultAsync( x => x.Id == customerModifications.Id );

                OnCustomerUpdated.OnNext( found );
            }
            catch( WampException )
            {
                throw;
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, $"{this}.CustomerUpdate(): failed to update customer in database" );
                throw new WampException( "erp.error.internalservererror", "ERROR! An internal server error occured" );
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.CustomerUpdate() finished" );
            }
        }

        [WampProcedure( "erp.crm.customer.replace" )]
        public async Task CustomerReplace( Customer customer )
        {
            try
            {
                log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.CustomerReplace() entered" );

                if( customer == null )
                    throw new WampException( "erp.crm.customer.error.invalidobject", "ERROR! Customer cannot be null" );
                if( string.IsNullOrEmpty( customer.Id ) )
                    throw new WampException( "erp.crm.customer.error.invalidid", "ERROR! Id of Customer cannot be null or empty" );

                var rep = Database.Get<Customer>();

                log.LogTrace( Event.VARIABLEGET, $"{this}.CustomerReplace(): looking for existing custyomer with id: '{customer.Id}'" );
                var found = await rep.Query.FirstOrDefaultAsync( x => x.Id == customer.Id );
                if( found == null )
                    throw new WampException( "erp.crm.customer.error.invalidid", $"ERROR! No customer with the id: '{ customer.Id }'" );

                await rep.ReplaceAsync( customer );

                OnCustomerUpdated.OnNext( customer );
            }
            catch( WampException )
            {
                throw;
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, $"{this}.CustomerReplace(): failed to replace customer in database" );
                throw new WampException( "erp.error.internalservererror", "ERROR! An internal server error occured" );
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.CustomerReplace() finished" );
            }
        }

        [WampProcedure( "erp.crm.customer.delete" )]
        public async Task CustomerDelete( string id )
        {
            log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.CustomerDelete() entered" );

            if( string.IsNullOrEmpty( id ) )
                throw new ArgumentNullException( "id" );

            var rep = Database.Get<Customer>();

            log.LogTrace( Event.VARIABLEGET, $"{this}.CustomerDelete(): looking for existing customer with id: '{id}'" );
            var found = await rep.Query.FirstOrDefaultAsync( x => x.Id == id );
            if( found == null )
                throw new WampException( new Dictionary<string, object>(), "erp.crm.customer.error.invalidid", $"ERROR! No customer with the id: '{ id }'", null );

            try
            {
                await rep.DeleteAsync( id );

                OnCustomerDeleted.OnNext( id );
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, "ERROR! Failed to delete customer in database" );
                throw;
            }

            log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.CustomerDelete() finished" );
        }

        #endregion

        #region Campaign

        [WampProcedure( "erp.inventory.campaign.create" )]
        public async Task<string> CampaignCreate( Campaign campaign )
        {
            log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.CampaignCreate() entered" );

            if( campaign == null )
                throw new ArgumentNullException( "campaign" );
            if( !string.IsNullOrEmpty( campaign.Id ) )
                throw new WampException( new Dictionary<string, object>(), "erp.inventory.campaign.error.invalidid", $"ERROR! Cannot add a new campaign with an already assigned id", null );

            var rep = Database.Get<Campaign>();

            campaign.DiscountType = DiscountType.Campaign;

            try
            {
                await rep.InsertAsync( campaign );

                OnCampaignAdded.OnNext( campaign );
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, "ERROR! Failed to insert campaign into database" );
                throw;
            }

            log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.CampaignCreate() finished" );

            return campaign.Id;
        }

        [WampProcedure( "erp.inventory.campaign.getstate" )]
        public async Task<GetStateResult<Campaign>> CampaignGetState( int first = 0, int count = 50, bool sortOrderAscending = true, string sortField = "Id", List<FilterData> filters = null, List<string> idList = null, bool includeList = true )
        {
            log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.CampaignGetState() entered" );

            try
            {
                return await GetObjectState<Campaign>( first, count, sortOrderAscending, sortField, filters, idList: idList, includeList: includeList );
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, "ERROR! Failed to get state of campaign from database" );
                throw new Exception( "ERROR! An internal server error occured" );
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.CampaignGetState() finished" );
            }
        }

        [WampProcedure( "erp.inventory.campaign.getitems" )]
        public async Task<List<CampaignItem>> CampaignGetItems( string campaignId, int first = 0, int count = 50 )
        {
            try
            {
                log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.CampaignGetItems() entered" );

                var campaignRepo = await Database.GetAsync<Campaign>();

                var found = await campaignRepo.Query.FirstOrDefaultAsync( x => x.Id == campaignId );
                if( found == null )
                    throw new ArgumentException( $"ERROR! Failed to find campaign with given id '{campaignId}'" );

                return found.Products.Skip( first ).Take( count ).ToList();
            }
            catch( ArgumentException )
            {
                throw;
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, "ERROR! Failed to get items of campaign from database" );
                throw new Exception( "ERROR! An internal server error occured" );
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.CampaignGetItems() finished" );
            }
        }

        [WampProcedure( "erp.inventory.campaign.update" )]
        public async Task CampaignUpdate( Campaign campaignModifications, UpdateAction[] actions = null )
        {
            try
            {
                log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.CampaignUpdate() entered" );

                if( campaignModifications == null || string.IsNullOrEmpty( campaignModifications.Id ) )
                    throw new WampException( "erp.inventory.campaign.error.invalidcampaign", "ERROR! Campaign was null or the id is empty" );

                var campaignRepo = await Database.GetAsync<Campaign>();

                var found = await campaignRepo.Query.FirstOrDefaultAsync( x => x.Id == campaignModifications.Id );
                if( found == null )
                    throw new WampException( "erp.inventory.campaign.error.invalidid", $"ERROR! Failed to find campaign with given id '{campaignModifications.Id}'" );

                await campaignRepo.UpdateAsync( campaignModifications, actions );

                found = await campaignRepo.Query.FirstOrDefaultAsync( x => x.Id == campaignModifications.Id );

                OnCampaignUpdated.OnNext( found );
            }
            catch( WampException )
            {
                throw;
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, "ERROR! Failed to update campaign in database" );
                throw new Exception( "ERROR! An internal server error occured" );
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.CampaignUpdate() finished" );
            }
        }

        [WampProcedure( "erp.inventory.campaign.replace" )]
        public async Task CampaignReplace( Campaign campaign )
        {
            try
            {
                log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.CampaignReplace() entered" );

                if( campaign == null )
                    throw new WampException( "erp.inventory.campaign.error.invalidobject", "ERROR! Campaign cannot be null" );
                if( string.IsNullOrEmpty( campaign.Id ) )
                    throw new WampException( "erp.inventory.campaign.error.invalidid", "ERROR! Id of Campaign cannot be null or empty" );

                var rep = Database.Get<Campaign>();

                log.LogTrace( Event.VARIABLEGET, $"{this}.CampaignReplace(): looking for existing campaign with id: '{campaign.Id}'" );
                var found = await rep.Query.FirstOrDefaultAsync( x => x.Id == campaign.Id );
                if( found == null )
                    throw new WampException( "erp.inventory.campaign.error.invalidid", $"ERROR! No campaign with the id: '{ campaign.Id }'" );

                await rep.ReplaceAsync( campaign );

                OnCampaignUpdated.OnNext( campaign );
            }
            catch( WampException )
            {
                throw;
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, $"{this}.CampaignReplace(): failed to replace campaign in database" );
                throw new WampException( "erp.error.internalservererror", "ERROR! An internal server error occured" );
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.CampaignReplace() finished" );
            }
        }

        [WampProcedure( "erp.inventory.campaign.delete" )]
        public async Task CampaignDelete( string id )
        {
            log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.CampaignDelete() entered" );

            if( string.IsNullOrEmpty( id ) )
                throw new ArgumentNullException( "id" );

            var rep = Database.Get<Campaign>();

            log.LogTrace( Event.VARIABLEGET, $"{this}.CampaignDelete(): looking for existing campaign with id: '{id}'" );
            var found = await rep.Query.FirstOrDefaultAsync( x => x.Id == id );
            if( found == null )
                throw new WampException( new Dictionary<string, object>(), "erp.inventory.campaign.error.invalidid", $"ERROR! No campaign with the id: '{ id }'", null );

            try
            {
                await rep.DeleteAsync( id );

                OnCampaignDeleted.OnNext( id );
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, "ERROR! Failed to delete campaign in database" );
                throw;
            }

            log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.CampaignDelete() finished" );
        }

        #endregion

        #region Discount

        [WampProcedure( "erp.inventory.discount.create" )]
        public async Task<string> DiscountCreate( DiscountBase discount )
        {
            log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.DiscountCreate() entered" );

            if( !Enum.IsDefined( typeof( DiscountType ), discount.DiscountType ) )
                throw new Exception( $"ERROR! Unknown DiscountType value of '{discount.DiscountType}'" );
            switch( discount.DiscountType )
            {
                case DiscountType.Campaign:
                    throw new Exception( "ERROR! Attempting to create a campaign with discount.create is not supported" );
                case DiscountType.Package:
                    {
                        var unpackagedDiscount = discount as PackageDiscount;
                        if( unpackagedDiscount.Products == null || unpackagedDiscount.Products.Count == 0 )
                            throw new Exception( $"ERROR! PackageDiscount.Items cannot be null or empty" );

                        break;
                    }
                case DiscountType.Quantity:
                    {
                        var unpackagedDiscount = discount as QuantityDiscount;
                        if( unpackagedDiscount.Products == null || unpackagedDiscount.Products.Count == 0 )
                            throw new Exception( $"ERROR! QuantityDiscount.Items cannot be null or empty" );

                        break;
                    }
                default:
                    throw new Exception( $"ERROR! Unknown DiscountType of '{ discount.DiscountType }'" );
            }
            /*if( discount.FromDateTime.CompareTo( new DateTime( DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day ) ) <= -1 )
                throw new Exception(  )*/

            var rep = Database.Get<DiscountBase>();

            try
            {
                await rep.InsertAsync( discount );

                OnDiscountAdded.OnNext( discount );
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, "ERROR! Failed to insert discount into database" );
                throw;
            }

            log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.DiscountCreate() finished" );

            return discount.Id;
        }

        [WampProcedure( "erp.inventory.discount.getstate" )]
        public async Task<GetStateResult<DiscountBase>> DiscountGetState( int first = 0, int count = 50, bool sortOrderAscending = true, string sortField = "Id", List<FilterData> filters = null )
        {
            log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.DiscountGetState() entered" );

            try
            {
                return await GetObjectState<DiscountBase>( first, count, sortOrderAscending, sortField, filters );
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, "ERROR! Failed to get state of discount from database" );
                throw new Exception( "ERROR! An internal server error occured" );
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.DiscountGetState() finished" );
            }
        }

        [WampProcedure( "erp.inventory.discount.getitems" )]
        public async Task<List<object>> DiscountGetItems( string discountId )
        {
            try
            {
                log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.DiscountGetItems() entered" );

                var repo = await Database.GetAsync<DiscountBase>();

                var found = await repo.Query.FirstOrDefaultAsync( x => x.Id == discountId );
                if( found == null )
                    throw new ArgumentException( $"ERROR! Failed to find discount with given id '{discountId}'" );

                switch( found.DiscountType )
                {
                    case DiscountType.Package:
                        return ( found as PackageDiscount ).Products.Cast<object>().ToList();
                    case DiscountType.Quantity:
                        return ( found as QuantityDiscount ).Products.Cast<object>().ToList();
                    default:
                        return null;
                }
            }
            catch( ArgumentException )
            {
                throw;
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, "ERROR! Failed to get items of discount from database" );
                throw new Exception( "ERROR! An internal server error occured" );
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.DiscountGetItems() finished" );
            }
        }

        [WampProcedure( "erp.inventory.discount.replace" )]
        public async Task DiscountReplace( DiscountBase discount )
        {
            try
            {
                log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.DiscountReplace() entered" );

                if( discount == null )
                    throw new WampException( "erp.inventory.discount.error.invalidobject", "ERROR! Discount cannot be null" );
                if( string.IsNullOrEmpty( discount.Id ) )
                    throw new WampException( "erp.inventory.discount.error.invalidid", "ERROR! Id of Discount cannot be null or empty" );

                var rep = Database.Get<DiscountBase>();

                log.LogTrace( Event.VARIABLEGET, $"{this}.DiscountReplace(): looking for existing discount with id: '{discount.Id}'" );
                var found = await rep.Query.FirstOrDefaultAsync( x => x.Id == discount.Id );
                if( found == null )
                    throw new WampException( "erp.inventory.productbrand.error.invalidid", $"ERROR! No discount with the id: '{ discount.Id }'" );

                await rep.ReplaceAsync( discount );

                OnDiscountUpdated.OnNext( discount );
            }
            catch( WampException )
            {
                throw;
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, $"{this}.DiscountReplace(): failed to replace discount in database" );
                throw new WampException( "erp.error.internalservererror", "ERROR! An internal server error occured" );
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.DiscountReplace() finished" );
            }
        }

        [WampProcedure( "erp.inventory.discount.update" )]
        public async Task DiscountUpdate( DiscountBase discountModifications, UpdateAction[] actions = null )
        {
            try
            {
                log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.DiscountUpdate() entered" );

                if( discountModifications == null )
                    throw new WampException( "erp.inventory.discount.error.invalidobject", "ERROR! Discount cannot be null" );
                if( string.IsNullOrEmpty( discountModifications.Id ) )
                    throw new WampException( "erp.inventory.discount.error.invalidid", "ERROR! Id of Discount cannot be null or empty" );

                var repo = await Database.GetAsync<DiscountBase>();

                var found = await repo.Query.FirstOrDefaultAsync( x => x.Id == discountModifications.Id );
                if( found == null )
                    throw new WampException( "erp.inventory.discount.error.invalidid", $"ERROR! Failed to find discount with given id '{discountModifications.Id}'" );

                await repo.UpdateAsync( discountModifications, actions );

                found = await repo.Query.FirstOrDefaultAsync( x => x.Id == discountModifications.Id );

                OnDiscountUpdated.OnNext( found );
            }
            catch( WampException )
            {
                throw;
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, $"{this}.DiscountUpdate(): failed to update discount in database" );
                throw new WampException( "erp.error.internalservererror", "ERROR! An internal server error occured" );
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.DiscountUpdate() finished" );
            }

            //try
            //{
            //    log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.DiscountUpdate() entered" );

            //    if( discountModifications == null || string.IsNullOrEmpty( discountModifications.Id ) )
            //        throw new WampException( "erp.inventory.discount.error.invalid_discount", "ERROR! Discount was null or the id is empty" );

            //    var discountRepo = await Database.GetAsync<DiscountBase>();

            //    var found = await discountRepo.Query.FirstOrDefaultAsync( x => x.Id == discountModifications.Id );
            //    if( found == null )
            //        throw new WampException( "erp.inventory.discount.error.invalid_id", $"ERROR! Failed to find discount with given id '{discountModifications.Id}'" );

            //    await discountRepo.UpdateAsync( discountModifications, actions );

            //    found = await discountRepo.Query.FirstOrDefaultAsync( x => x.Id == discountModifications.Id );

            //    OnDiscountUpdated.OnNext( found );
            //}
            //catch( WampException )
            //{
            //    throw;
            //}
            //catch( Exception e )
            //{
            //    log.LogError( Event.EXCEPTIONCAUGHT, e, "ERROR! Failed to update discount in database" );
            //    throw new Exception( "ERROR! An internal server error occured" );
            //}
            //finally
            //{
            //    log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.DiscountUpdate() finished" );
            //}
        }

        [WampProcedure( "erp.inventory.discount.delete" )]
        public async Task DiscountDelete( string id )
        {
            log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.DiscountDelete() entered" );

            if( string.IsNullOrEmpty( id ) )
                throw new ArgumentNullException( "id" );

            var rep = Database.Get<DiscountBase>();

            log.LogTrace( Event.VARIABLEGET, $"{this}.DiscountDelete(): looking for existing discount with id: '{id}'" );
            var found = await rep.Query.FirstOrDefaultAsync( x => x.Id == id );
            if( found == null )
                throw new Exception( $"ERROR! No discount with the id: '{ id }' found" );

            try
            {
                await rep.DeleteAsync( id );

                OnDiscountDeleted.OnNext( id );
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, "ERROR! Failed to delete discount in database" );
                throw;
            }

            log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.DiscountDelete() finished" );
        }

        #endregion

        #region Country

        [WampProcedure( "erp.country.create" )]
        public async Task CountryCreate( Models.ERP.Country country )
        {
            log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.CountryAdd() entered" );

            if( country == null )
                throw new ArgumentNullException( "country" );
            if( !string.IsNullOrEmpty( country.Id ) )
                throw new WampException( new Dictionary<string, object>(), "erp.country.error.invalidid", $"ERROR! Cannot add a new country with an already assigned id", null );

            var rep = await this.UserDatabase.GetAsync<Models.ERP.Country>();

            if( await rep.Query.FirstOrDefaultAsync( x => x.Name == country.Name || x.ISOCode == country.ISOCode ) != null )
                throw new WampException( new Dictionary<string, object>(), "erp.country.error.invalidcountry", $"ERROR! Cannot add a country with an already existing name or ISOCode", null );

            try
            {
                await rep.InsertAsync( country );
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, "ERROR! Failed to insert country into database" );
                throw;
            }

            log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.CountryAdd() finished" );
        }

        [WampProcedure( "erp.country.get" )]
        public async Task<Models.ERP.Country> CountryGet( string countryId )
        {
            log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.CountryGet() entered" );

            if( string.IsNullOrEmpty( countryId ) )
                throw new ArgumentException( "ERROR! countryId cannot be null or empty" );

            var rep = await this.UserDatabase.GetAsync<Models.ERP.Country>();

            var foundCountry = await rep.Query.FirstOrDefaultAsync( x => x.Id == countryId );

            if( foundCountry == null )
                throw new ArgumentException( $"ERROR! Failed to find country with id '{countryId}'" );

            log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.CountryGet() finished" );

            return foundCountry;
        }

        [WampProcedure( "erp.country.getstate" )]
        public async Task<GetStateResult<Models.ERP.Country>> CountryGetState( int first = 0, int count = 0, bool sortOrderAscending = true, string sortField = "Id", List<FilterData> filters = null, List<string> idList = null, bool includeList = true )
        {
            log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.CountryGetState() entered" );

            try
            {
                return await GetObjectState<Models.ERP.Country>( first, count, sortOrderAscending, sortField, filters, idList: idList, includeList: includeList, database: UserDatabase );
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, "ERROR! Failed to get state of campaign from database" );
                throw new Exception( "ERROR! An internal server error occured" );
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.CountryGetState() finished" );
            }
        }

        [WampProcedure( "erp.country.update" )]
        public async Task CountryUpdate( Models.ERP.Country countryModifications, UpdateAction[] actions = null )
        {
            try
            {
                log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.CountryUpdate() entered" );

                if( countryModifications == null || string.IsNullOrEmpty( countryModifications.Id ) )
                    throw new WampException( "erp.country.error.invalidcountry", "ERROR! country was null or the id is empty" );

                var campaignRepo = await Database.GetAsync<Models.ERP.Country>();

                var found = await campaignRepo.Query.FirstOrDefaultAsync( x => x.Id == countryModifications.Id );
                if( found == null )
                    throw new WampException( "erp.country.error.invalidid", $"ERROR! Failed to find country with given id '{countryModifications.Id}'" );

                await campaignRepo.UpdateAsync( countryModifications, actions );

                found = await campaignRepo.Query.FirstOrDefaultAsync( x => x.Id == countryModifications.Id );

            }
            catch( WampException )
            {
                throw;
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, "ERROR! Failed to update country in database" );
                throw new Exception( "ERROR! An internal server error occured" );
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.CountryUpdate() finished" );
            }
        }

        [WampProcedure( "erp.country.replace" )]
        public async Task CountryReplace( Models.ERP.Country country )
        {
            try
            {
                log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.CountryReplace() entered" );

                if( country == null )
                    throw new WampException( "erp.country.error.invalidobject", "ERROR! Country cannot be null" );
                if( string.IsNullOrEmpty( country.Id ) )
                    throw new WampException( "erp.country.error.invalidid", "ERROR! Id of Country cannot be null or empty" );

                var rep = Database.Get<Models.ERP.Country>();

                log.LogTrace( Event.VARIABLEGET, $"{this}.CountryReplace(): looking for existing country with id: '{country.Id}'" );
                var found = await rep.Query.FirstOrDefaultAsync( x => x.Id == country.Id );
                if( found == null )
                    throw new WampException( "erp.country.error.invalidid", $"ERROR! No country with the id: '{ country.Id }'" );

                await rep.ReplaceAsync( country );

            }
            catch( WampException )
            {
                throw;
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, $"{this}.CountryReplace(): failed to replace country in database" );
                throw new WampException( "erp.error.internalservererror", "ERROR! An internal server error occured" );
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.CountryReplace() finished" );
            }
        }

        #endregion

        #region Category

        [WampProcedure( "erp.inventory.category.create" )]
        public async Task<string> CategoryCreate( Category category )
        {
            try
            {
                log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.CategoryCreate() entered" );

                if( category == null )
                    throw new WampException( "erp.inventory.category.error.invalidobject", "ERROR! The Category object cannot be null" );
                if( !string.IsNullOrEmpty( category.Id ) )
                    throw new WampException( "erp.inventory.category.error.invalidid", $"ERROR! Cannot add a new Category with an already assigned id" );

                var rep = Database.Get<Category>();

                await rep.InsertAsync( category );

                OnCategoryAdded.OnNext( category );

                return category.Id;
            }
            catch( WampException )
            {
                throw;
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, $"{this}.CategoryCreate(): failed to insert category into database" );
                throw new Exception( "ERROR! An internal server error occured" );
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.CategoryCreate() finished" );
            }
        }

        [WampProcedure( "erp.inventory.category.getstate" )]
        public async Task<GetStateResult<Category>> CategoryGetState( int first = 0, int count = 0, bool sortOrderAscending = true, string sortField = "Id", List<FilterData> filters = null, List<string> idList = null, bool includeList = true )
        {
            try
            {
                log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.CategoryGetState() entered" );

                return await GetObjectState<Category>( first, count, sortOrderAscending, sortField, filters, idList: idList, includeList: includeList );
            }
            catch( WampException )
            {
                throw;
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, $"{this}.CategoryGetState(): failed to get state of category from database" );
                throw new Exception( "ERROR! An internal server error occured" );
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.CategoryGetState() finished" );
            }
        }

        [WampProcedure( "erp.inventory.category.update" )]
        public async Task CategoryUpdate( Category categoryModifications, UpdateAction[] actions = null )
        {
            try
            {
                log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.CategoryUpdate() entered" );

                if( categoryModifications == null )
                    throw new WampException( "erp.inventory.category.event.invalidobject", "ERROR! Category cannot be null" );
                if( string.IsNullOrEmpty( categoryModifications.Id ) )
                    throw new WampException( "erp.inventory.category.event.invalidid", "ERROR! Id of Category cannot be null or empty" );

                var repo = await Database.GetAsync<Category>();

                var found = await repo.Query.FirstOrDefaultAsync( x => x.Id == categoryModifications.Id );
                if( found == null )
                    throw new WampException( "erp.inventory.category.error.invalidid", $"ERROR! Failed to find category with given id '{categoryModifications.Id}'" );

                await repo.UpdateAsync( categoryModifications, actions );

                found = await repo.Query.FirstOrDefaultAsync( x => x.Id == categoryModifications.Id );

                OnCategoryUpdated.OnNext( found );
            }
            catch( WampException )
            {
                throw;
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, $"{this}.CategoryUpdate(): failed to update category in database" );
                throw new Exception( "ERROR! An internal server error occured" );
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.CategoryUpdate() finished" );
            }
        }

        [WampProcedure( "erp.inventory.category.replace" )]
        public async Task CategoryReplace( Category category )
        {
            try
            {
                log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.CategoryReplace() entered" );

                if( category == null )
                    throw new WampException( "erp.inventory.category.error.invalidobject", "ERROR! The Category object cannot be null" );

                var rep = Database.Get<Category>();

                log.LogTrace( Event.VARIABLEGET, $"{this}.CategoryReplace(): looking for existing category with id: '{category.Id}'" );
                var found = await rep.Query.FirstOrDefaultAsync( x => x.Id == category.Id );
                if( found == null )
                    throw new WampException( "erp.inventory.category.error.invalidid", $"ERROR! No category with the id: '{ category.Id }' found" );

                await rep.ReplaceAsync( category );

                OnCategoryUpdated.OnNext( category );
            }
            catch( WampException )
            {
                throw;
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, $"{this}.CategoryReplace(): failed to replace category in database" );
                throw new Exception( "ERROR! An internal server error occured" );
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.CategoryReplace() finished" );
            }
        }

        [WampProcedure( "erp.inventory.category.delete" )]
        public async Task CategoryDelete( string id )
        {
            try
            {
                log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.CategoryDelete() entered" );

                if( string.IsNullOrEmpty( id ) )
                    throw new WampException( "erp.inventory.category.event.invalidid", "id" );

                var rep = Database.Get<Category>();

                log.LogTrace( Event.VARIABLEGET, $"{this}.CategoryDelete(): looking for existing category with id: '{ id }'" );
                var found = await rep.Query.FirstOrDefaultAsync( x => x.Id == id );
                if( found == null )
                    throw new WampException( "erp.inventory.category.error.invalidid", $"ERROR! No category with the id: '{ id }' found" );

                await rep.DeleteAsync( id );

                OnCategoryDeleted.OnNext( id );
            }
            catch( WampException )
            {
                throw;
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, $"{this}.CategoryDelete(): failed to delete category in database" );
                throw new Exception( "ERROR! An internal server error occured" );
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.CategoryDelete() finished" );
            }
        }


        [WampProcedure( "erp.inventory.category.deleteAll" )]
        public async Task CategoryDeleteAll( string[] ids )
        {
            try
            {
                log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.CategoryDeleteAll() entered" );

                if( ids == null || ids.Length == 0 || ids.Any( x => string.IsNullOrEmpty( x ) ) )
                    throw new WampException( "erp.inventory.category.event.invalidid", $"ERROR! ids cannot be null, be empty or contain an empty string" );

                var rep = Database.Get<Category>();


                //Any way to avoid looking at each ID individually to check if they exist?
                foreach( var id in ids )
                {
                    log.LogTrace( Event.VARIABLEGET, $"T{this}.CategoryDeleteAll(): looking for existing category with id: '{id}' " );
                    var found = await rep.Query.FirstOrDefaultAsync( x => x.Id == id );
                    if( found == null )
                        throw new WampException( "erp.inventory.category.error.invalidid", $"ERROR! no category with the id: '{id}' found" );

                    await rep.DeleteAsync( id );

                    OnCategoryDeleted.OnNext( id );

                }

            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, $"{this}.CategoryDeleteAll(): fialed to delete category in database" );
                throw new Exception( "ERROR! An internal server error occured" );
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.CategoryDeleteAll finished" );
            }

        }


        [WampProcedure( "erp.inventory.category.getspecifications" )]
        public async Task<ProductSpecification[]> CategoryGetSpecifications( string[] ids )
        {
            try
            {
                log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.CategoryGetSpecifications() entered" );

                if( ids == null || ids.Length == 0 || ids.Any( x => string.IsNullOrEmpty( x ) ) )
                    throw new WampException( "erp.inventory.category.event.invalidid", $"ERROR! Ids cannot be null, be empty or contain an empty string" );

                var rep = Database.Get<Category>();

                List<DBRef> specifications = new List<DBRef>();
                foreach( var id in ids )
                {
                    log.LogTrace( Event.VARIABLEGET, $"{this}.CategoryGetSpecifications(): looking for existing category with id: '{ id }'" );
                    var found = await rep.Query.FirstOrDefaultAsync( x => x.Id == id );
                    if( found == null )
                        throw new WampException( "erp.inventory.category.error.invalidid", $"ERROR! No category with the id: '{ id }' found" );

                    //Future optimization possibility would be to change the structure to use a materialized path: https://docs.mongodb.com/manual/tutorial/model-tree-structures-with-materialized-paths/
                    if( found.Specifications != null && found.Specifications.Length > 0 )
                        specifications = specifications.Concat( found.Specifications ).ToList();
                    while( found.ParentCategory != null )
                    {
                        log.LogTrace( Event.VARIABLEGET, $"{this}.CategoryGetSpecifications(): looking for parent with id: '{ found.ParentCategory.Id }' from category '{ found.Id }'" );
                        found = await rep.Query.FirstOrDefaultAsync( x => x.Id == found.ParentCategory.Id );
                        if( found.Specifications != null && found.Specifications.Length > 0 )
                            specifications.AddRange( found.Specifications );
                    }
                }


                if( specifications == null || !specifications.Any() || !specifications.Where( x => x != null ).Any() )
                    return null;

                return ( await GetObjectState<ProductSpecification>( idList: specifications.Where( x => x != null ).Select( x => x.Id ).Distinct(), includeList: true ) ).Result.ToArray();
            }
            catch( WampException )
            {
                throw;
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, $"{this}.CategoryGetSpecifications(): failed to find specifications in database" );
                throw new Exception( "ERROR! An internal server error occured" );
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.CategoryGetSpecifications() finished" );
            }
        }

        #endregion

        #region WebshopStatus

        [WampProcedure( "erp.inventory.product.webshopstatus.getstate" )]
        public async Task<GetStateResult<EnumProductWebshopStatus>> webshopGetState( int first = 0, int count = 0, bool sortOrderAscending = true, string sortField = "Id", List<FilterData> filters = null, List<string> idList = null, bool includeList = true )
        {
            log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.webshopStatusGetState() entered" );

            try
            {
                return await GetObjectState<EnumProductWebshopStatus>( first, count, sortOrderAscending, sortField, filters, idList: idList, includeList: includeList );
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, "ERROR! Failed to get state of campaign from database" );
                throw new Exception( "ERROR! An internal server error occured" );
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.webshopStatusGetState() finished" );
            }
        }

        #endregion

        #region EAN

        //[WampProcedure("erp.ean.generate")]
        //public string EanGenerate(EanState ean)
        //{
        //    log.LogDebug(Event.ENTEREDFUNCTION, $"{this}.EANGenerate Entered");

        //    if (string.IsNullOrEmpty(ean.Ean))
        //        throw new ArgumentNullException("ean");

        //    char[] digits;
        //    digits = new char[13];

        //    string returnValue = null;
        //    var eanChars = ean.Ean.ToCharArray();
        //    var length = ean.Ean.Length;

        //    for (int i = 0; i < eanChars.Length; i++)
        //    {
        //        digits[i] = eanChars[i];
        //    }

        //    if (length < 7)
        //    {
        //        for (int i = length; i < 7; i++)
        //        {
        //            digits[i] = char.Parse(0.ToString());
        //        }
        //        length = 7;
        //    }

        //    //throw new InvalidDataException( "Could not generate a new EAN number based on the input." );
        //    Random rand = new Random();

        //    for (int i = length; i < digits.Length - 1; i++)
        //    {
        //        //if( digits[ i ] != null )
        //        digits[i] = char.Parse(rand.Next(0, 10).ToString());
        //    }

        //    int sum = 0;
        //    for (int i = 0; i < digits.Length - 1; i++)
        //    {
        //        if (i % 2 == 0)
        //        {
        //            sum += (int)Char.GetNumericValue(digits[i]);
        //        }
        //        else
        //        {
        //            sum += (int)Char.GetNumericValue(digits[i]) * 3;
        //        }
        //    }        

        //    if ( sum % 10 == 0 )
        //    {
        //        for( int i = 0; i < 12; i++ )
        //        {
        //            returnValue = string.Concat( returnValue, digits[ i ] );
        //        }
        //        returnValue = string.Concat( returnValue, "0" );
        //    }
        //    else
        //    {
        //        int checknum = 10 - ( sum % 10 );
        //        for( int i = 0; i < 12; i++ )
        //        {
        //            returnValue += digits[ i ];
        //        }
        //        returnValue += checknum;
        //    }
        //    return returnValue;
        //}

        [WampProcedure( "erp.ean.verifyandgenerate" )]
        public EanState EanVerify( string ean )
        {
            log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.EanVerify Entered" );

            if( string.IsNullOrEmpty( ean ) )
            {
                var errorState = new EanState();
                errorState.State = State.EMPTY;
                errorState.Ean = "";
                return errorState;
            }
            EanState returnValue = new EanState();
            returnValue.Ean = ean;

            char[] digits;
            digits = new char[ 13 ];
            if( ean.Length == 13 )
                digits = ean.ToCharArray();
            if( ean.Length != 13 )
            {
                returnValue.State = State.INVALID;

                var eanChars = ean.ToCharArray();
                var length = ean.Length;

                for( int i = 0; i < eanChars.Length; i++ )
                {
                    digits[ i ] = eanChars[ i ];
                }

                if( length < 7 )
                {
                    for( int i = length; i < 7; i++ )
                    {
                        digits[ i ] = char.Parse( 0.ToString() );
                    }
                    length = 7;
                }

                //throw new InvalidDataException( "Could not generate a new EAN number based on the input." );
                Random rand = new Random();

                for( int i = length; i < digits.Length - 1; i++ )
                {
                    //if( digits[ i ] != null )
                    digits[ i ] = char.Parse( rand.Next( 0, 10 ).ToString() );
                }
            }


            int sum = 0;
            for( int i = 0; i < digits.Length - 1; i++ )
            {
                if( i % 2 == 0 )
                {
                    sum += (int) char.GetNumericValue( digits[ i ] );
                }
                else
                {
                    sum += (int) char.GetNumericValue( digits[ i ] ) * 3;
                }
            }
            if( sum % 10 == 0 )
            {
                if( !( (int) char.GetNumericValue( digits[ 12 ] ) == 0 ) )
                {
                    returnValue.State = State.INVALID;
                    digits[ 12 ] = Convert.ToChar( "0" );
                    for( int i = 0; i < 12; i++ )
                    {
                        returnValue.Ean += digits[ i ];
                    }
                }
            }
            else
            {
                int checknum = 10 - ( sum % 10 );
                if( !( (int) char.GetNumericValue( digits[ 12 ] ) == checknum ) )
                {
                    returnValue.State = State.INVALID;
                    var tempChecknum = digits[ 12 ] = Convert.ToChar( string.Concat( checknum ) );
                    var tempEan = "";
                    for( int i = 0; i < 12; i++ )
                    {
                        tempEan += digits[ i ];
                    }
                    tempEan += tempChecknum;
                    returnValue.Ean = tempEan;
                }
            }
            var rep = Database.Get<Product>();
            var product = rep.Query.FirstOrDefault( x => x.EAN == returnValue.Ean );
            if( product != null )
            {
                var eans = rep.Query.Select( x => x.EAN ).ToArray().OrderBy( x => x );
                if( rep.Query.Count() > 1 )
                {
                    returnValue.State = State.USED;
                    long? prevEan = null;
                    foreach( var item in eans )
                    {
                        long? diff = long.Parse( item ) - prevEan;
                        if( diff != null && diff > 1 )
                        {
                            var tempDigits = returnValue.Ean.ToCharArray();
                            var incrementedDigit = int.Parse( string.Concat( tempDigits[ 11 ] ) ) + 1;
                            tempDigits[ 11 ] = Convert.ToChar( string.Concat( incrementedDigit ) );
                            var tempEan = "";
                            for( int i = 0; i < 12; i++ )
                            {
                                tempEan += tempDigits[ i ];
                            }
                            returnValue.Ean = tempEan;
                        }
                        prevEan = long.Parse( item );
                    }
                }
                else
                {
                    returnValue.Ean = string.Concat( ean + 1 );
                    returnValue.State = State.VALID;
                }
            }
            return returnValue;
        }

        #endregion

        #region PriceSchedule

        [WampProcedure( "erp.inventory.product.priceschedule.create" )]
        public async Task<string> PriceScheduleCreate( PriceSchedule schedule )
        {
            log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.PriceScheduleCreate() entered" );

            if( schedule == null )
                throw new ArgumentNullException( "schedule" );
            if( !string.IsNullOrEmpty( schedule.Id ) )
                throw new Exception( "ERROR! Cannot add a new price schedule with an already assigned id" );
            if( schedule.FromDateTime < new DateTime( DateTime.UtcNow.Year, DateTime.UtcNow.Month, DateTime.UtcNow.Day ) )
                throw new ArgumentException( "ERROR! Cannot create new price schedule that is earlier than today" );

            try
            {
                var repo = await Database.GetAsync<PriceSchedule>();

                schedule.Executed = false;

                await repo.InsertAsync( schedule );

                priceScheduleExecuteCreateTimer( schedule );

                OnPriceScheduleAdded.OnNext( schedule );

                return schedule.Id;
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, "ERROR! Failed to create new price schedule in database" );
                throw new Exception( "ERROR! An internal server error occured" );
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.PriceScheduleCreate() finished" );
            }
        }

        [WampProcedure( "erp.inventory.product.priceschedule.getstate" )]
        public async Task<GetStateResult<PriceSchedule>> PriceScheduleGetState( int first = 0, int count = 50, bool sortOrderAscending = true, string sortField = "Id", List<FilterData> filters = null )
        {
            log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.PriceScheduleGetState() entered" );

            try
            {
                return await GetObjectState<PriceSchedule>( first, count, sortOrderAscending, sortField, filters );
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, "ERROR! Failed to get state of price schedule from database" );
                throw new Exception( "ERROR! An internal server error occured" );
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.PriceScheduleGetState() finished" );
            }
        }

        [WampProcedure( "erp.inventory.product.priceschedule.getitems" )]
        public async Task<List<PriceScheduleItem>> PriceScheduleGetItems( string priceScheduleId, int first = 0, int count = 50 )
        {
            try
            {
                log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.PriceScheduleGetItems() entered" );

                var repo = await Database.GetAsync<PriceSchedule>();

                var found = await repo.Query.FirstOrDefaultAsync( x => x.Id == priceScheduleId );
                if( found == null )
                    throw new ArgumentException( $"ERROR! Failed to find PriceSchedule with given id '{priceScheduleId}'" );

                return found.Items.Skip( first ).Take( count ).ToList();
            }
            catch( ArgumentException )
            {
                throw;
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, "ERROR! Failed to get items of PriceSchedule from database" );
                throw new Exception( "ERROR! An internal server error occured" );
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.PriceScheduleGetItems() finished" );
            }
        }

        [WampProcedure( "erp.inventory.product.priceschedule.addproduct" )]
        public async Task<string> PriceScheduleAddProduct( string campaignId, string productId, decimal newPrice )
        {
            try
            {
                log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.PriceScheduleAddProduct() entered" );

                var campaignRepo = await Database.GetAsync<Campaign>();
                var foundCampaign = await campaignRepo.Query.FirstOrDefaultAsync( x => x.Id == campaignId );
                if( foundCampaign == null )
                    throw new ArgumentException( $"ERROR! Failed to find Campaign with given id '{campaignId}" );

                var priceScheduleRepo = await Database.GetAsync<PriceSchedule>();
                var foundPriceschedule = await priceScheduleRepo.Query.FirstOrDefaultAsync( x => x.Campaign != null && x.Campaign.Id == campaignId );
                if( foundPriceschedule == null )
                {
                    var newPriceSchedule = new PriceSchedule();
                    var fromDateTime = foundCampaign.EndDateTime;
                    if( !fromDateTime.HasValue || fromDateTime.Value < new DateTime( DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day ) )
                        fromDateTime = new DateTime( DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day );
                    newPriceSchedule.FromDateTime = fromDateTime.Value;
                    newPriceSchedule.Items = new List<PriceScheduleItem>();
                    newPriceSchedule.Items.Add( new PriceScheduleItem() { NewPrice = (float) newPrice, Product = new DBRef( productId ) } );
                    newPriceSchedule.Campaign = new DBRef( campaignId );
                    newPriceSchedule.Name = "Pris opdateringer for " + foundCampaign.Name;

                    return await PriceScheduleCreate( newPriceSchedule );
                }
                else
                {
                    foundPriceschedule.Items.Add( new PriceScheduleItem() { NewPrice = (float) newPrice, Product = new DBRef( productId ) } );

                    await PriceScheduleUpdate( foundPriceschedule );

                    return foundPriceschedule.Id;
                }
            }
            catch( ArgumentException )
            {
                throw;
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, "ERROR! Failed to add new product to PriceSchedule in database" );
                throw new Exception( "ERROR! An internal server error occured" );
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.PriceScheduleAddProduct() finished" );
            }
        }

        [WampProcedure( "erp.inventory.product.priceschedule.getproductpricesforcampaign" )]
        public async Task<Dictionary<string, decimal>> PriceScheduleGetProductPricesForCampaign( string campaignId )
        {
            try
            {
                log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.PriceScheduleGetProductPricesForCampaign() entered" );

                var repo = await Database.GetAsync<PriceSchedule>();
                var foundPriceSchedule = await repo.Query.FirstOrDefaultAsync( x => x.Campaign != null && x.Campaign.Id == campaignId );
                if( foundPriceSchedule == null )
                    return null;

                return foundPriceSchedule.Items.ToDictionary( x => x.Product.Id, x => (decimal) x.NewPrice );
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, "ERROR! Failed to find priceschedule in database" );
                throw new Exception( "ERROR! An internal server error occured" );
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.PriceScheduleGetProductPricesForCampaign() finished" );
            }
        }

        [WampProcedure( "erp.inventory.product.priceschedule.getnonaddedproductsforcampaign" )]
        public async Task<Product[]> PriceScheduleGetNonAddedProductsForCampaign( string campaignId, List<FilterData> filterData = null, Dictionary<string, bool> localChanges = null )
        {
            try
            {
                log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.PriceScheduleGetNonAddedProductsForCampaign() entered" );

                var priceScheduleRepo = await Database.GetAsync<PriceSchedule>();
                var foundPriceSchedule = await ( priceScheduleRepo.Query as IMongoQueryable<PriceSchedule> ).FirstOrDefaultAsync( x => x.Campaign != null && x.Campaign.Id == campaignId );

                var campaignRepo = await Database.GetAsync<Campaign>();
                var foundCampaign = await ( campaignRepo.Query as IMongoQueryable<Campaign> ).FirstOrDefaultAsync( x => x.Id == campaignId );
                if( foundCampaign == null )
                    return null;

                var excludedIds = new Dictionary<string, bool>();

                foreach( var kvp in localChanges )
                {
                    if( kvp.Value )
                        excludedIds[ kvp.Key ] = true;
                }

                foreach( var item in ( foundPriceSchedule?.Items ?? new List<PriceScheduleItem>() ).Select( x => x.Product.Id ) )
                {
                    bool value;
                    if( localChanges.TryGetValue( item, out value ) && !value )
                        continue;

                    excludedIds[ item ] = true;
                }

                var includeIds = foundCampaign.Products.Where( x => !excludedIds.ContainsKey( x.Product.Id ) ).Select( x => x.Product.Id ).ToList();

                if( includeIds == null || includeIds.Count == 0 )
                    return null;

                return ( await ProductGetState( count: 10, filters: filterData, idList: includeIds ) ).Result.ToArray();
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, "ERROR! Failed to find products in database" );
                throw new Exception( "ERROR! An internal server error occured" );
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.PriceScheduleGetNonAddedProductsForCampaign() finished" );
            }
        }

        /*private async Task<PriceSchedule> priceScheduleUpdateInternal( PriceSchedule schedule, bool skipDateCheck, PriceSchedule alreadyExistingSchedule = null )
        {
            log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.priceScheduleUpdateInternal() entered" );

            if( skipDateCheck && alreadyExistingSchedule == null )
                throw new ArgumentNullException( "alreadyExistingSchedule", "ERROR! alreadyExistingSchedule cannot be null when skipDateCheck is true" );

            try
            {
                var repo = await database.GetAsync<PriceSchedule>();
                PriceSchedule existingList = null;
                if( !skipDateCheck )
                {
                    existingList = await repo.Query.FirstOrDefaultAsync( x => x.FromDateTime == schedule.FromDateTime );
                }
                else
                    existingList = alreadyExistingSchedule;

                if( !existingList.Id.Equals( schedule.Id, StringComparison.OrdinalIgnoreCase ) )
                {
                    existingList.Name += " OG " + schedule.Name;
                }

                if( existingList != null )
                {
                    //Union schedule.Items into ExistingList.Items is relevant because in case there is duplicates because we want to keep the NEWEST, which would be the items of schedule
                    existingList.Items = schedule.Items.Union( existingList.Items ).ToList();
                }
                existingList.Executed = false;

                await repo.UpdateAsync( existingList );

                priceScheduleExecuteCreateTimer( existingList );

                return existingList;
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.priceScheduleUpdateInternal() finished" );
            }
        }*/

        private static ConcurrentDictionary<string, Timer> priceScheduleTimerDict = new ConcurrentDictionary<string, Timer>();
        private void priceScheduleExecuteCreateTimer( PriceSchedule schedule )
        {
            log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.priceScheduleExecuteCreateTimer() entered" );

            var current = DateTime.Now;
            var timeLeft = schedule.FromDateTime.TimeOfDay - current.TimeOfDay;
            if( timeLeft < TimeSpan.Zero )
            {
                Task.Run( () => priceScheduleExecute( schedule, false ) );
            }
            else
            {
                priceScheduleTimerDict.AddOrUpdate( schedule.Id, new Timer( x => priceScheduleExecute( schedule ), null, timeLeft, Timeout.InfiniteTimeSpan ), ( id, oldTimer ) => new Timer( x => priceScheduleExecute( schedule ), null, timeLeft, Timeout.InfiniteTimeSpan ) );
                log.LogInformation( $"{this}.priceScheduleExecuteCreateTimer(): Created new timer to execute in '{timeLeft}' for schedule with Id '{schedule.Id}'" );
            }

            log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.priceScheduleExecuteCreateTimer() finished" );
        }

        private void priceScheduleExecute( PriceSchedule schedule, bool removeFromDict = true )
        {
            log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.priceScheduleExecute() entered for schedule {schedule.Id}" );

            try
            {
                var productrep = Database.Get<Product>();
                var ids = schedule.Items.Select( x => x.Product.Id );
                var products = productrep.Query.Where( x => ids.Contains( x.Id ) ).ToDictionary( x => x.Id );

                foreach( var item in schedule.Items )
                {
                    products[ item.Product.Id ].SalesPrice = item.NewPrice;
                }

                productrep.ReplaceMany( products.Values.ToArray() );

                var priceScheduleRep = Database.Get<PriceSchedule>();
                schedule.Executed = true;
                priceScheduleRep.Replace( schedule );

                if( removeFromDict )
                {
                    Timer timer = null;
                    if( !priceScheduleTimerDict.TryRemove( schedule.Id, out timer ) )
                        log.LogError( Event.ERROR, $"ERROR! Attempted to remove timer for {schedule.Id} from the timer dictionary, but failed" );
                }
            }
            catch( Exception e )
            {
                var newTime = DateTime.Now.AddHours( 1 );
                log.LogError( Event.EXCEPTIONCAUGHT, e, $"ERROR! An error occured while attempting to execute PriceSchedule with id '{schedule.Id}'. Rescheduling to '{newTime}'" );

                priceScheduleExecuteCreateTimer( schedule );

                throw new Exception( $"ERROR! An internal error occured while attempting to execute PriceSchedule with id '{schedule.Id}'" );
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.priceScheduleExecute() finished for schedule {schedule.Id}" );
            }
        }

        [WampProcedure( "erp.inventory.product.priceschedule.update" )]
        public async Task PriceScheduleUpdate( PriceSchedule schedule )
        {
            log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.PriceScheduleGetState() entered" );

            if( schedule == null )
                throw new ArgumentNullException( "schedule" );
            if( string.IsNullOrEmpty( schedule.Id ) )
                throw new Exception( "ERROR! Cannot update a price schedule without an already assigned id" );
            if( schedule.FromDateTime < new DateTime( DateTime.UtcNow.Year, DateTime.UtcNow.Month, DateTime.UtcNow.Day ) )
                throw new ArgumentException( "ERROR! Cannot set date of price schedule to a date earlier than today" );

            var rep = await Database.GetAsync<PriceSchedule>();
            var found = await rep.Query.FirstOrDefaultAsync( x => x.Id == schedule.Id );
            if( found == null )
                throw new ArgumentException( $"ERROR! No priceschedule with the id '{ schedule.Id }' found" );

            try
            {
                await rep.ReplaceAsync( schedule );

                OnPriceScheduleUpdated.OnNext( schedule );
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, "ERROR! Failed to update price schedule in database" );
                throw new Exception( "ERROR! An internal server error occured" );
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.PriceScheduleGetState() finished" );
            }
        }

        /*[WampProcedure( "erp.inventory.product.priceschedule.updateproductlist" )]
        public async Task PriceScheduleUpdate( string priceScheduleId, List<UpdateAction> updates )
        {
            try
            {
                var rep = await database.GetAsync<PriceSchedule>();

            }
            catch( Exception )
            {

                throw;
            }
        }*/

        [WampProcedure( "erp.inventory.product.priceschedule.delete" )]
        public async Task PriceScheduleDelete( string id )
        {
            log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.PriceScheduleDelete() entered" );

            if( string.IsNullOrEmpty( id ) )
                throw new ArgumentNullException( "id" );

            var rep = await Database.GetAsync<PriceSchedule>();
            log.LogTrace( Event.VARIABLEGET, $"{this}.PriceScheduleDelete(): looking for existing price schedule with id: '{id}'" );
            var found = await rep.Query.FirstOrDefaultAsync( x => x.Id == id );
            if( found == null )
                throw new Exception( $"ERROR! No campaign with the id: '{ id }'" );

            try
            {
                await rep.DeleteAsync( id );

                OnPriceScheduleDeleted.OnNext( id );
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, "ERROR! Failed to delete campaign in database" );
                throw;
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.PriceScheduleDelete() finished" );
            }
        }

        #endregion

        #region Valizo

        #region Order

        [WampProcedure( "erp.valizo.order.orderpaid" )]
        public async Task ValizoOrderPaid( string orderId )
        {
            log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.ValizoOrderCreate() entered" );

            if( string.IsNullOrEmpty( orderId ) )
                throw new ArgumentNullException( "orderId" );

            try
            {
                var rep = Database.Get<Order>();

                var foundOrder = await rep.Query.FirstOrDefaultAsync( x => x.Id == orderId );
                if( foundOrder == null )
                    throw new Exception( $"ERROR! Failed to find order with the id '{orderId}'" );

                foundOrder.PaidDateTime = DateTime.Now;

                await rep.ReplaceAsync( foundOrder );
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, "ERROR! Failed to update order in database" );
                throw new Exception( "ERROR! An internal server error occured while attempting to change payment of order" );
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.ValizoOrderCreate() finished" );
            }
        }

        [WampProcedure( "erp.valizo.order.create" )]
        public async Task<string> ValizoOrderCreate( Models.Valizo.Order order )
        {
            log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.ValizoOrderCreate() entered" );

            if( order == null )
                throw new ArgumentNullException( "order" );
            if( !string.IsNullOrEmpty( order.Id ) )
                throw new Exception( $"ERROR! Cannot add a new order with an already assigned id" );
            if( !Regex.Match( order.Contact.Email, @"^[A-Za-z0-9._%+-]+@(?:[A-Za-z0-9-]+\.)+[A-Za-z]{2,}$" ).Success )
                throw new Exception( $"ERROR! Email '{ order.Contact.Email }' is not a valid email" );

            try
            {
                var rep = Database.Get<Models.Valizo.Order>();

                await rep.InsertAsync( order );
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, "ERROR! Failed to insert order into database" );
                throw new Exception( "ERROR! An internal server error occured" );
            }

            try
            {
                var mandrill = new MandrillApi( "qZKgftWvFMJIKbc0AuK79w" );
                var message = new MandrillMessage( "noreply@mjpsoftware.dk", order.Contact.Email, $"Valizo ordre, upload af pas", $"Hej { order.Contact.FirstName + order.Contact.MiddleName + order.Contact.LastName }, { Environment.NewLine } { Environment.NewLine } her skal du uploade billeder af dit pas og/eller visum: http://81.7.186.84:8080/test/uploadPassport/index.html?orderId={order.Id}" );
                var result = await mandrill.Messages.SendAsync( message );

                return order.Id;
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, "ERROR! Failed to send email using Mandrill" );
                throw new Exception( "ERROR! An internal server error occured while attempting to send email" );
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.ValizoOrderCreate() finished" );
            }
        }

        [WampProcedure( "erp.valizo.order.get" )]
        public async Task<Order> ValizoOrderGet( string id )
        {
            log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.ValizoOrderGet() entered" );

            if( string.IsNullOrEmpty( id ) )
                throw new ArgumentNullException( "id" );

            try
            {
                var rep = Database.Get<Models.Valizo.Order>();

                var foundOrder = await rep.Query.FirstOrDefaultAsync( x => x.Id == id );
                if( foundOrder == null )
                    throw new ArgumentException( $"ERROR! Failed to find order with the id '{id}'" );

                return foundOrder;
            }
            catch( ArgumentException )
            {
                throw;
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, "ERROR! Failed to find order in database" );
                throw new Exception( "ERROR! An internal server error occured" );
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.ValizoOrderGet() finished" );
            }
        }

        [WampProcedure( "erp.valizo.order.getlist" )]
        public async Task<Order[]> ValizoOrderGetList()
        {
            try
            {
                log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.ValizoOrderGet() entered" );

                var rep = Database.Get<Models.Valizo.Order>();

                return await rep.Query.ToAsyncEnumerable().ToArray();
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, "ERROR! Failed to get list of orders from database" );
                throw new Exception( "ERROR! An internal server error occured" );
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.ValizoOrderGet() finished" );
            }
        }

        #endregion

        #region Airport

        [WampProcedure( "erp.valizo.airport.create" )]
        public async Task<string> ValizoAirportCreate( Airport airport )
        {
            log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.ValizoAirportCreate() entered" );

            if( airport == null )
                throw new ArgumentNullException( "airport" );
            if( !string.IsNullOrEmpty( airport.Id ) )
                throw new Exception( $"ERROR! Cannot add a new airport with an already assigned id" );
            if( string.IsNullOrEmpty( airport.Name ) )
                throw new Exception( $"ERROR! Cannot add a new airport with an empty Name" );
            if( string.IsNullOrEmpty( airport.ShortName ) )
                throw new Exception( $"ERROR! Cannot add a new airport with an empty ShortName" );


            var rep = await Database.GetAsync<Airport>();
            if( await rep.Query.FirstOrDefaultAsync( x => x.Name == airport.Name || x.ShortName == airport.ShortName ) != null )
                throw new Exception( $"ERROR! Cannot add a airport with an already existing name or shortName" );

            try
            {
                await rep.InsertAsync( airport );

                OnValizoAirportAdded.OnNext( airport );

                return airport.Id;
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, "ERROR! Failed to insert airport into database" );
                throw;
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.ValizoAirportCreate() finished" );

            }
        }

        [WampProcedure( "erp.valizo.airport.get" )]
        public async Task<Airport> ValizoAirportGet( string airportId )
        {
            log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.ValizoAirportGet() entered" );

            if( string.IsNullOrEmpty( airportId ) )
                throw new ArgumentException( "ERROR! id cannot be null or empty" );

            var rep = await Database.GetAsync<Airport>();

            var found = await rep.Query.FirstOrDefaultAsync( x => x.Id == airportId );
            if( found == null )
                throw new ArgumentException( $"ERROR! Failed to find airport with id '{airportId}'" );

            log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.ValizoAirportGet() finished" );

            return found;
        }

        [WampProcedure( "erp.valizo.airport.getlist" )]
        public async Task<Airport[]> ValizoAirportGetList()
        {
            log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.ValizoAirportGetList() entered" );

            var list = ( await Database.GetAsync<Airport>() ).Query.ToArray();

            log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.ValizoAirportGetList() finished" );

            return list;
        }

        [WampProcedure( "erp.valizo.airport.update" )]
        public async Task ValizoAirportUpdate( Airport airport )
        {
            log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.ValizoAirportUpdate() entered" );

            if( airport == null )
                throw new ArgumentNullException( "airport" );

            var rep = Database.Get<Airport>();

            log.LogTrace( Event.VARIABLEGET, $"{this}.ValizoAirportUpdate(): looking for existing airport with id: '{airport.Id}'" );
            var found = await rep.Query.FirstOrDefaultAsync( x => x.Id == airport.Id );
            if( found == null )
                throw new Exception( $"ERROR! No airport with the id: '{ airport.Id }' found" );

            try
            {
                await rep.ReplaceAsync( airport );

                OnValizoAirportUpdated.OnNext( airport );
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, "ERROR! Failed to update airport in database" );
                throw;
            }
            finally
            {
                log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.ValizoAirportUpdate() finished" );
            }
        }

        [WampProcedure( "erp.valizo.airport.delete" )]
        public async Task ValizoAirportDelete( string id )
        {
            log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.ValizoAirportDelete() entered" );

            if( string.IsNullOrEmpty( id ) )
                throw new ArgumentNullException( "id" );

            var rep = Database.Get<Airport>();

            log.LogTrace( Event.VARIABLEGET, $"{this}.ValizoAirportDelete(): looking for existing airport with id: '{id}'" );
            var found = await rep.Query.FirstOrDefaultAsync( x => x.Id == id );
            if( found == null )
                throw new Exception( $"ERROR! No airport with the id: '{ id }' found" );

            try
            {
                await rep.DeleteAsync( id );

                OnValizoAirportDeleted.OnNext( id );
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, "ERROR! Failed to delete airport in database" );
                throw;
            }

            log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.ValizoAirportDelete() finished" );
        }

        #endregion

        #region Airline

        [WampProcedure( "erp.valizo.airline.create" )]
        public async Task<string> ValizoAirlineCreate( Airline airline )
        {
            log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.ValizoAirlineCreate() entered" );

            if( airline == null )
                throw new ArgumentNullException( "airport" );
            if( !string.IsNullOrEmpty( airline.Id ) )
                throw new Exception( $"ERROR! Cannot add a new airline with an already assigned id" );

            var rep = await Database.GetAsync<Airline>();
            if( await rep.Query.FirstOrDefaultAsync( x => x.Name == airline.Name || x.FlightInitials == airline.FlightInitials ) != null )
                throw new Exception( $"ERROR! Cannot add a airline with an already existing name or initials" );

            try
            {
                await rep.InsertAsync( airline );
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, "ERROR! Failed to insert airline into database" );
                throw;
            }

            log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.ValizoAirlineCreate() finished" );

            return airline.Id;
        }

        [WampProcedure( "erp.valizo.airline.get" )]
        public async Task<Airline> ValizoAirlineGet( string airlineId )
        {
            log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.ValizoAirlineGet() entered" );

            if( string.IsNullOrEmpty( airlineId ) )
                throw new ArgumentException( "ERROR! airportId cannot be 0 or less" );

            var rep = await Database.GetAsync<Airline>();

            var foundAirline = await rep.Query.FirstOrDefaultAsync( x => x.Id == airlineId );
            if( foundAirline == null )
                throw new ArgumentException( $"ERROR! Failed to find airline with airlineId '{airlineId}'" );

            log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.ValizoAirlineGet() finished" );

            return foundAirline;
        }

        [WampProcedure( "erp.valizo.airline.getlist" )]
        public async Task<Airline[]> ValizoAirlineGetList()
        {
            log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.ValizoAirlineGetList() entered" );

            var airlines = ( await Database.GetAsync<Airline>() ).Query.ToArray();

            log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.ValizoAirlineGetList() finished" );

            return airlines;
        }

        [WampProcedure( "erp.valizo.airline.update" )]
        public async Task ValizoAirlineUpdate( Airline airline )
        {
            log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.ValizoAirlineUpdate() entered" );

            if( airline == null )
                throw new ArgumentNullException( "airline" );
            if( string.IsNullOrEmpty( airline.Id ) )
                throw new Exception( $"ERROR! Cannot update airline without an existing id" );

            var rep = await Database.GetAsync<Airline>();

            log.LogTrace( Event.VARIABLEGET, $"{this}.ValizoAirlineUpdate(): looking for existing airline with id: '{airline.Id}'" );
            var found = await rep.Query.FirstOrDefaultAsync( x => x.Id == airline.Id );
            if( found == null )
                throw new Exception( $"ERROR! No airline with the id: '{ airline.Id }' found" );

            try
            {
                await rep.ReplaceAsync( airline );
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, $"{this}.ValizoAirlineUpdate(): ERROR! Failed to update airline in database" );
                throw new Exception( "ERROR! An internal server error occured" );
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.ValizoAirlineUpdate() finished" );
            }
        }

        [WampProcedure( "erp.valizo.airline.delete" )]
        public async Task ValizoAirlineDelete( string airlineId )
        {
            log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.ValizoAirlineDelete() entered" );

            if( string.IsNullOrEmpty( airlineId ) )
                throw new ArgumentNullException( "airlineId" );

            var rep = await Database.GetAsync<Airline>();

            log.LogTrace( Event.VARIABLEGET, $"{this}.ValizoAirlineDelete(): looking for existing airline with id: '{airlineId}'" );
            var found = await rep.Query.FirstOrDefaultAsync( x => x.Id == airlineId );
            if( found == null )
                throw new Exception( $"ERROR! No airline with the id: '{ airlineId }' found" );

            try
            {
                await rep.DeleteAsync( airlineId );
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, $"{this}.ValizoAirlineDelete(): ERROR! Failed to delete airline in database" );
                throw new Exception( "ERROR! An internal server error occured" );
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.ValizoAirlineDelete() finished" );
            }
        }

        #endregion

        #region HotelPriceTable

        [WampProcedure( "erp.valizo.hotel.create" )]
        public async Task<string> ValizoHotelCreate( HotelPriceTable hotel )
        {
            log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.ValizoHotelCreate() entered" );

            if( hotel == null )
                throw new ArgumentNullException( "hotel" );
            if( !string.IsNullOrEmpty( hotel.Id ) )
                throw new WampException( new Dictionary<string, object>(), "erp.hotel.error.invalidid", $"ERROR! Cannot add a new hotel with an already assigned id", null );

            var rep = await Database.GetAsync<HotelPriceTable>();
            if( await rep.Query.FirstOrDefaultAsync( x => x.Id == hotel.Id || x.Name == hotel.Name ) != null )
                throw new WampException( new Dictionary<string, object>(), "erp.hotel.error.invalidhotel", $"ERROR! Cannot add a hotel with an already existing name", null );

            try
            {
                await rep.InsertAsync( hotel );
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, "ERROR! Failed to insert hotel into database" );
                throw;
            }

            log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.ValizoHotelCreate() finished" );

            return hotel.Id;
        }

        [WampProcedure( "erp.valizo.hotel.get" )]
        public async Task<HotelPriceTable> ValizoHotelGet( string id )
        {
            log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.ValizoHotelGet() entered" );

            if( string.IsNullOrEmpty( id ) )
                throw new ArgumentException( "ERROR! id cannot be 0 or less" );

            var rep = await Database.GetAsync<HotelPriceTable>();

            var foundHotel = await rep.Query.FirstOrDefaultAsync( x => x.Id == id );
            if( foundHotel == null )
                throw new ArgumentException( $"ERROR! Failed to find hotel with id '{id}'" );

            log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.ValizoHotelGet() finished" );

            return foundHotel;
        }

        [WampProcedure( "erp.valizo.hotel.getlist" )]
        public async Task<HotelPriceTable[]> ValizoHotelGetList()
        {
            log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.ValizoHotelGetList() entered" );

            var hotels = ( await Database.GetAsync<HotelPriceTable>() ).Query.ToArray();

            log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.ValizoHotelGetList() finished" );

            return hotels;
        }

        [WampProcedure( "erp.valizo.hotel.update" )]
        public async Task ValizoHotelUpdate( HotelPriceTable hotel )
        {
            log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.ValizoHotelUpdate() entered" );

            if( hotel == null )
                throw new ArgumentNullException( "hotel" );
            if( string.IsNullOrEmpty( hotel.Id ) )
                throw new Exception( $"ERROR! Cannot update hotel without an existing id" );

            var rep = await Database.GetAsync<HotelPriceTable>();

            log.LogTrace( Event.VARIABLEGET, $"{this}.ValizoHotelUpdate(): looking for existing hotel with id: '{hotel.Id}'" );
            var found = await rep.Query.FirstOrDefaultAsync( x => x.Id == hotel.Id );
            if( found == null )
                throw new Exception( $"ERROR! No hotel with the id: '{ hotel.Id }' found" );

            try
            {
                await rep.ReplaceAsync( hotel );
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, $"{this}.ValizoHotelUpdate(): ERROR! Failed to update hotel in database" );
                throw new Exception( "ERROR! An internal server error occured" );
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.ValizoHotelUpdate() finished" );
            }
        }

        [WampProcedure( "erp.valizo.hotel.delete" )]
        public async Task ValizoHotelDelete( string hotelId )
        {
            log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.ValizoHotelDelete() entered" );

            if( string.IsNullOrEmpty( hotelId ) )
                throw new ArgumentNullException( "hotelId" );

            var rep = await Database.GetAsync<HotelPriceTable>();

            log.LogTrace( Event.VARIABLEGET, $"{this}.ValizoHotelDelete(): looking for existing hotel with id: '{hotelId}'" );
            var found = await rep.Query.FirstOrDefaultAsync( x => x.Id == hotelId );
            if( found == null )
                throw new Exception( $"ERROR! No hotel with the id: '{ hotelId }' found" );

            try
            {
                await rep.DeleteAsync( hotelId );
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, $"{this}.ValizoHotelDelete(): ERROR! Failed to delete hotel in database" );
                throw new Exception( "ERROR! An internal server error occured" );
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.ValizoHotelDelete() finished" );
            }
        }

        #endregion

        #region ZipPriceTable

        [WampProcedure( "erp.valizo.zip.create" )]
        public async Task<string> ValizoZipCreate( ZipPriceTable zip )
        {
            log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.ValizoZipCreate() entered" );

            if( zip == null )
                throw new ArgumentNullException( "zip" );
            if( !string.IsNullOrEmpty( zip.Id ) )
                throw new WampException( new Dictionary<string, object>(), "erp.zip.error.invalidid", $"ERROR! Cannot add a new zip with an already assigned id", null );

            var rep = await Database.GetAsync<ZipPriceTable>();
            if( await rep.Query.FirstOrDefaultAsync( x => x.Id == zip.Id || x.Value == zip.Value ) != null )
                throw new WampException( new Dictionary<string, object>(), "erp.zip.error.invalidzip", $"ERROR! Cannot add a zip with an already existing value", null );

            try
            {
                await rep.InsertAsync( zip );
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, "ERROR! Failed to insert zip into database" );
                throw;
            }

            log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.ValizoZipCreate() finished" );

            return zip.Id;
        }

        [WampProcedure( "erp.valizo.zip.get" )]
        public async Task<ZipPriceTable> ValizoZipGet( string id )
        {
            log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.ValizoZipGet() entered" );

            if( string.IsNullOrEmpty( id ) )
                throw new ArgumentException( "ERROR! id cannot be 0 or less" );

            var rep = await Database.GetAsync<ZipPriceTable>();

            var foundZip = await rep.Query.FirstOrDefaultAsync( x => x.Id == id );
            if( foundZip == null )
                throw new ArgumentException( $"ERROR! Failed to find zip with id '{id}'" );

            log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.ValizoZipGet() finished" );

            return foundZip;
        }

        [WampProcedure( "erp.valizo.zip.getlist" )]
        public async Task<ZipPriceTable[]> ValizoZipGetList()
        {
            log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.ValizoZipGetList() entered" );

            var zips = ( await Database.GetAsync<ZipPriceTable>() ).Query.ToArray();

            log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.ValizoZipGetList() finished" );

            return zips;
        }

        [WampProcedure( "erp.valizo.zip.update" )]
        public async Task ValizoZipUpdate( ZipPriceTable zip )
        {
            log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.ValizoZipUpdate() entered" );

            if( zip == null )
                throw new ArgumentNullException( "zip" );
            if( string.IsNullOrEmpty( zip.Id ) )
                throw new Exception( $"ERROR! Cannot update zip without an existing id" );

            var rep = await Database.GetAsync<ZipPriceTable>();

            log.LogTrace( Event.VARIABLEGET, $"{this}.ValizoZipUpdate(): looking for existing zip with id: '{zip.Id}'" );
            var found = await rep.Query.FirstOrDefaultAsync( x => x.Id == zip.Id );
            if( found == null )
                throw new Exception( $"ERROR! No zip with the id: '{ zip.Id }' found" );

            try
            {
                await rep.ReplaceAsync( zip );
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, $"{this}.ValizoZipUpdate(): ERROR! Failed to update zip in database" );
                throw new Exception( "ERROR! An internal server error occured" );
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.ValizoZipUpdate() finished" );
            }
        }

        [WampProcedure( "erp.valizo.zip.delete" )]
        public async Task ValizoZipDelete( string zipId )
        {
            log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.ValizoZipDelete() entered" );

            if( string.IsNullOrEmpty( zipId ) )
                throw new ArgumentNullException( "zipId" );

            var rep = await Database.GetAsync<ZipPriceTable>();

            log.LogTrace( Event.VARIABLEGET, $"{this}.ValizoZipDelete(): looking for existing zip with id: '{zipId}'" );
            var found = await rep.Query.FirstOrDefaultAsync( x => x.Id == zipId );
            if( found == null )
                throw new Exception( $"ERROR! No zip with the id: '{ zipId }' found" );

            try
            {
                await rep.DeleteAsync( zipId );
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, $"{this}.ValizoZipDelete(): ERROR! Failed to delete zip in database" );
                throw new Exception( "ERROR! An internal server error occured" );
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.ValizoZipDelete() finished" );
            }
        }

        #endregion

        #region Country

        [WampProcedure( "erp.valizo.country.create" )]
        public async Task ValizoCountryCreate( Models.Valizo.Country country )
        {
            log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.ValizoCountryCreate() entered" );

            if( country == null )
                throw new ArgumentNullException( "country" );
            if( !string.IsNullOrEmpty( country.Id ) )
                throw new Exception( $"ERROR! Cannot add a new country with an already assigned id" );

            var rep = await this.Database.GetAsync<Models.Valizo.Country>();

            if( await rep.Query.FirstOrDefaultAsync( x => x.Name == country.Name || x.ISOCode == country.ISOCode ) != null )
                throw new Exception( $"ERROR! Cannot add a country with an already existing name or ISOCode" );

            try
            {
                await rep.InsertAsync( country );
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, "ERROR! Failed to insert country into database" );
                throw new Exception( "ERROR! An internal server error occured" );
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.ValizoCountryCreate() finished" );
            }
        }

        [WampProcedure( "erp.valizo.country.get" )]
        public async Task<Models.Valizo.Country> ValizoCountryGet( string countryId )
        {
            log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.ValizoCountryGet() entered" );

            if( string.IsNullOrEmpty( countryId ) )
                throw new ArgumentException( "ERROR! countryId cannot be null or empty" );

            try
            {
                var rep = await this.Database.GetAsync<Models.Valizo.Country>();

                var found = await rep.Query.FirstOrDefaultAsync( x => x.Id == countryId );

                if( found == null )
                    throw new ArgumentException( $"ERROR! Failed to find country with id '{countryId}'" );

                return found;
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, "ERROR! Failed to get country from database" );
                throw new Exception( "ERROR! An internal server error occured" );
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.ValizoCountryGet() finished" );
            }
        }

        [WampProcedure( "erp.valizo.country.getlist" )]
        public async Task<Models.Valizo.Country[]> ValizoCountryGetList()
        {
            try
            {
                log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.CountryGetList() entered" );

                var result = ( await Database.GetAsync<Models.Valizo.Country>() ).Query.ToArray();

                return result;
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, "ERROR! Failed to get list of countries from database" );
                throw new Exception( "ERROR! An internal server error occured" );
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.CountryGetList() finished" );
            }
        }

        [WampProcedure( "erp.valizo.country.update" )]
        public async Task ValizoCountryUpdate( Models.Valizo.Country country )
        {
            log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.ValizoCountryUpdate() entered" );

            if( country == null )
                throw new ArgumentNullException( "country" );
            if( string.IsNullOrEmpty( country.Id ) )
                throw new Exception( $"ERROR! Cannot update country without an existing id" );

            var rep = await this.Database.GetAsync<Models.Valizo.Country>();

            log.LogTrace( Event.VARIABLEGET, $"{this}.ValizoCountryUpdate(): looking for existing country with id: '{country.Id}'" );
            var found = await rep.Query.FirstOrDefaultAsync( x => x.Id == country.Id );
            if( found == null )
                throw new Exception( $"ERROR! No country with the id: '{ country.Id }' found" );

            try
            {
                await rep.ReplaceAsync( country );
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, $"{this}.ValizoCountryUpdate(): ERROR! Failed to update country in database" );
                throw new Exception( "ERROR! An internal server error occured" );
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.ValizoCountryUpdate() finished" );
            }
        }

        [WampProcedure( "erp.valizo.country.delete" )]
        public async Task ValizoCountryDelete( string countryId )
        {
            log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.ValizoCountryDelete() entered" );

            if( string.IsNullOrEmpty( countryId ) )
                throw new ArgumentNullException( "countryId" );

            var rep = await this.Database.GetAsync<Models.Valizo.Country>();

            log.LogTrace( Event.VARIABLEGET, $"{this}.ValizoCountryDelete(): looking for existing country with id: '{countryId}'" );
            var found = await rep.Query.FirstOrDefaultAsync( x => x.Id == countryId );
            if( found == null )
                throw new Exception( $"ERROR! No country with the id: '{ countryId }' found" );

            try
            {
                await rep.DeleteAsync( countryId );
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, $"{this}.ValizoZipDelete(): ERROR! Failed to delete country in database" );
                throw new Exception( "ERROR! An internal server error occured" );
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.ValizoCountryDelete() finished" );
            }
        }

        #endregion

        public async Task ValizoFileUploaded( FileUpload file )
        {
            log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.ValizoFileUploaded() entered" );

            if( file == null )
                throw new ArgumentNullException( "file" );
            var exceptionMessage = "";
            if( string.IsNullOrEmpty( file.FileName ) )
                exceptionMessage = "ERROR! FileName cannot be empty";
            else if( !File.Exists( ROUTERPATH + file.FileName ) )
                exceptionMessage = $"ERROR! No file could be found at 'routerpath\\{file.FileName}'";
            else if( string.IsNullOrEmpty( file.OrderId ) )
                exceptionMessage = "ERROR! OrderId cannot be empty";
            else if( file.PersonId < 0 )
                exceptionMessage = "ERROR! PersonId cannot be less than zero";

            if( !string.IsNullOrEmpty( exceptionMessage ) )
            {
                var exception = new WampException( new Dictionary<string, object>(), "erp.file.upload.errors.invalidfile", exceptionMessage, null );
                log.LogError( Event.EXCEPTIONCAUGHT, exception, exceptionMessage );
                throw exception;
            }

            var rep = await Database.GetAsync<Order>();

            var foundOrder = await rep.Query.FirstOrDefaultAsync( x => x.Id == file.OrderId );
            if( foundOrder == null )
            {
                exceptionMessage = $"ERROR! Could not find order with orderId '{file.OrderId}'";
                var exception = new WampException( new Dictionary<string, object>(), "erp.file.upload.errors.invalidfile", exceptionMessage, null );

                log.LogError( Event.EXCEPTIONCAUGHT, exception, exceptionMessage );
                throw exception;
            }

            if( File.Exists( VALIZOFILEPATH + foundOrder.Id + "\\lock.txt" ) )
                throw new WampException( new Dictionary<string, object>(), "erp.valizo.upload.errors.invalidfile", $"ERROR! The order with id '{foundOrder.Id}' is already being processed.", null );

            var newPath = VALIZOFILEPATH + file.OrderId + "\\" + file.PersonId + "\\" + ( file.IsVisa ? "visa" : file.IsFrontPage ? "frontpage" : "passport" ) + new System.IO.FileInfo( file.FileName ).Extension;
            var newPathInfo = new FileInfo( newPath );

            //Try-galore LUL
            try
            {
                //Check to see if the directory we're moving the file to, exists. If not, create it.
                try
                {
                    log.LogDebug( Event.INFO, $"{this}.ValizoFileUploaded(): checking if the directory at new path exists: {newPathInfo.Directory}" );
                    if( !newPathInfo.Directory.Exists )
                    {
                        log.LogDebug( Event.STATEMENTEXECUTED, $"{this}.ValizoFileUploaded(): creating directory at new path: {newPathInfo.Directory}" );
                        newPathInfo.Directory.Create();
                    }
                }
                catch( Exception e )
                {
                    log.LogError( Event.EXCEPTIONCAUGHT, e, "ERROR! An unknown exception occured while attempting to create directory at newPath" );
                    throw;
                }

                //Check to see if there's a file already at the path that we're moving to. If yes, delete it.
                try
                {
                    log.LogDebug( Event.INFO, $"{this}.ValizoFileUploaded(): checking if a file already exists at new path: {newPath}" );
                    if( newPathInfo.Exists )
                    {
                        log.LogDebug( Event.STATEMENTEXECUTED, $"{this}.ValizoFileUploaded(): deleting existing file at new path: {newPath}" );
                        File.Delete( newPath );
                    }
                }
                catch( Exception e )
                {
                    log.LogError( Event.EXCEPTIONCAUGHT, e, "ERROR! An unknown exception occured while attempting to delete existing file at new path" );
                    throw;
                }

                //Attempt to move the file to the new path.
                try
                {
                    log.LogDebug( Event.INFO, $"{this}.ValizoFileUploaded(): moving uploaded file { file.FileName } to { newPath }" );
                    File.Move( ROUTERPATH + file.FileName, newPath );
                }
                catch( Exception e )
                {
                    log.LogError( Event.EXCEPTIONCAUGHT, e, "ERROR! An unknown excpetion occured while attempting to move file to new path" );
                    throw;
                }
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.ValizoFileUploaded() finished" );
            }
        }

        [WampProcedure( "erp.valizo.order.filesready" )]
        public async Task ValizoFilesReady( string orderId )
        {
            log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.ValizoFilesReady() entered" );

            if( string.IsNullOrEmpty( orderId ) )
                throw new ArgumentNullException( "orderId" );
            var foundOrder = await ( await Database.GetAsync<Order>() ).Query.FirstOrDefaultAsync( x => x.Id == orderId );
            if( foundOrder == null )
                throw new WampException( new Dictionary<string, object>(), "erp.valizo.order.errors.invalidorder", $"ERROR! Could not finder order with orderId '{orderId}'", null );

            string outFormat = "txt";

            if( File.Exists( VALIZOFILEPATH + foundOrder.Id + "\\lock.txt" ) )
                throw new WampException( new Dictionary<string, object>(), "erp.valizo.order.errors.invalidorder", $"ERROR! The order with id '{orderId}' is already being processed.", null );
            else
            {

                var files = Directory.GetFiles( VALIZOFILEPATH + foundOrder.Id, "*", SearchOption.AllDirectories ).Select( x => new FileInfo( x ) ).Where( x => x.Extension.Trim( '.' ) != outFormat );
                var foundFrontPages = files.Count( x => x.Name.Substring( 0, x.Name.IndexOf( '.' ) ).Equals( "frontpage", StringComparison.OrdinalIgnoreCase ) );
                var foundPassports = files.Count( x => x.Name.Substring( 0, x.Name.IndexOf( '.' ) ).Equals( "passport", StringComparison.OrdinalIgnoreCase ) );
                var personAmount = foundOrder.Bookings.SelectMany( x => x.People ).Count();

                if( foundFrontPages != personAmount )
                    throw new Exception( "ERROR! Not all passengers have uploaded frontpage" );
                if( foundPassports != personAmount )
                    throw new Exception( "ERROR! Not all passengers have uploaded passports" );

                using( var stream = File.CreateText( VALIZOFILEPATH + foundOrder.Id + "\\lock.txt" ) )
                {
                    stream.Write( "" );
                    stream.Flush();
                }
            }

            try
            {
                await Task.Run( async () =>
                {
                    log.LogInformation( Event.INFO, $"{this}.ValizoFilesReady(): preparing to ocr files for orderId '{orderId}'" );

                    try
                    {
                        OCRProcessing tester = new OCRProcessing();

                        ProcessingModeEnum processingMode = ProcessingModeEnum.SinglePage;

                        string profile = "textExtraction";
                        string language = "danish";

                        if( string.IsNullOrEmpty( outFormat ) )
                        {
                            if( processingMode == ProcessingModeEnum.ProcessFields ||
                                processingMode == ProcessingModeEnum.ProcessTextField ||
                                processingMode == ProcessingModeEnum.ProcessMrz )
                                outFormat = "xml";
                            else
                                outFormat = "txt";
                        }

                        if( outFormat != "xml" &&
                            ( processingMode == ProcessingModeEnum.ProcessFields ||
                            processingMode == ProcessingModeEnum.ProcessTextField ) )
                        {
                            log.LogWarning( Event.WARNING, $"{this}.ValizoFilesReady(): only xml is supported as output format for field-level recognition. Settings outFormat to 'xml'" );

                            outFormat = "xml";
                        }

                        if( processingMode == ProcessingModeEnum.SinglePage || processingMode == ProcessingModeEnum.MultiPage )
                        {
                            ProcessingSettings settings = buildSettings( language, outFormat, profile );
                            settings.CustomOptions = null;

                            var message = new MandrillMessage( "noreply@mjpsoftware.dk", "mjp@mjpsoftware.dk" );

                            var sb = new StringBuilder();
                            sb.Append( "<html>" );
                            sb.Append( "<body>" );

                            sb.AppendLine( "Alle billeder er blevet OCR scannet og følgende information er blevet fundet:" );
                            sb.AppendLine();

                            var images = new List<MandrillImage>();
                            int id = 1;
                            foreach( var person in foundOrder.Bookings.SelectMany( x => x.People ) )
                            {
                                var sourceFolder = VALIZOFILEPATH + foundOrder.Id + "\\" + person.PersonId + "\\";

                                try
                                {
                                    tester.ProcessPath( sourceFolder, sourceFolder, settings, processingMode );
                                }
                                catch( Exception e )
                                {
                                    var errorMessage = $"ERROR! An internal error occured while processing files in {sourceFolder}";

                                    log.LogError( Event.EXCEPTIONCAUGHT, e, errorMessage );
                                    throw new WampException( new Dictionary<string, object>(), "erp.valizo.order.error.ocrerror", errorMessage, null );
                                }

                                sb.AppendLine( $"Passager nummer {person.PersonId} med navnet {person.Name} har følgende information:" );

                                /*var visaPath = sourceFolder + "\\visa." + outFormat;
                                if( System.IO.File.Exists( visaPath ) )
                                {
                                    sb.AppendLine();
                                    sb.AppendLine( "Visum:" );
                                    using( var file = System.IO.File.OpenText( visaPath ) )
                                    {
                                        var txtFile = file.ReadToEnd();
                                        var passportDataLine1 = Regex.Match( txtFile, @"P<[\\|'\w\d<]+" );
                                        var passportDataLine2 = Regex.Match( txtFile.Substring( passportDataLine1.Index + passportDataLine1.Length ), @"[\w\d<\\|']+" );

                                        sb.AppendLine( passportDataLine1.Value );
                                        sb.AppendLine( passportDataLine2.Value );
                                    }
                                }*/

                                sb.AppendLine();
                                sb.AppendLine( "Pas information fra OCR:" );

                                using( var file = System.IO.File.OpenText( sourceFolder + "\\passport." + outFormat ) )
                                {
                                    var txtFile = file.ReadToEnd();
                                    var passportDataLine1 = Regex.Match( txtFile, @"P<[\\|'\w\d<]+" );
                                    var passportDataLine2 = Regex.Match( txtFile.Substring( passportDataLine1.Index + passportDataLine1.Length ), @"[\w\d<\\|']+" );

                                    sb.AppendLine( passportDataLine1.Value );
                                    sb.AppendLine( passportDataLine2.Value );
                                }

                                sb.AppendLine();


                                var files = Directory.GetFiles( sourceFolder ).OrderBy( x => x ).Select( x => new FileInfo( x ) ).Where( x => x.Extension.Trim( '.' ) != outFormat );
                                foreach( var file in files )
                                {
                                    var name = file.Name.Substring( 0, file.Name.IndexOf( '.' ) );

                                    if( name.Equals( "frontpage", StringComparison.OrdinalIgnoreCase ) )
                                    {
                                        sb.AppendLine( "Forside" );
                                        sb.AppendLine( $"<img src=\"cid:{id}_frontpage\">" );
                                        images.Add( new MandrillImage( "image/" + file.Extension.Trim( '.' ), id + "_frontpage", File.ReadAllBytes( file.FullName ) ) );
                                        sb.AppendLine();
                                    }
                                    else if( name.Equals( "passport", StringComparison.OrdinalIgnoreCase ) )
                                    {
                                        sb.AppendLine( "Pas" );
                                        sb.AppendLine( $"<img src=\"cid:{id}_passport\">" );
                                        images.Add( new MandrillImage( "image/jpeg", id + "_passport", File.ReadAllBytes( file.FullName ) ) );
                                        sb.AppendLine();
                                    }
                                    else if( name.Equals( "visa", StringComparison.OrdinalIgnoreCase ) )
                                    {
                                        sb.AppendLine( "Visum" );
                                        sb.AppendLine( $"<img src=\"cid:{id}_visa\">" );
                                        images.Add( new MandrillImage( "image/" + file.Extension.Trim( '.' ), id + "_visa", File.ReadAllBytes( file.FullName ) ) );
                                        sb.AppendLine();
                                    }
                                    else
                                    {
                                        log.LogError( Event.ERROR, $"{this}.ValizoFilesReady(): ERROR! Unknown file found at {file.FullName}" );
                                        throw new Exception( "ERROR! Unknown file found in path" );
                                    }
                                }

                                sb.AppendLine();

                                id++;
                            }

                            message.Images = images;
                            sb.AppendLine( "</body>" );
                            sb.AppendLine( "</html>" );

                            log.LogInformation( Event.INFO, $"{this}.ValizoFilesReady(): attempting to send email for order id: '{orderId}'" );
                            var mandrill = new MandrillApi( "qZKgftWvFMJIKbc0AuK79w" );
                            message.Subject = $"Ordre id { orderId } har uploadet pas";
                            message.Html = sb.Replace( Environment.NewLine, "<br />" ).ToString();
                            var result = await mandrill.Messages.SendAsync( message ).TimeoutAfter( TimeSpan.FromSeconds( 30 ) );
                            log.LogInformation( Event.INFO, $"{this}.ValizoFilesReady(): finished sending email for order id: '{orderId}'" );
                        }
                    }
                    catch( Exception e )
                    {
                        Console.WriteLine( "Error: " );
                        Console.WriteLine( e.Message );
                    }
                    finally
                    {
                        File.Delete( VALIZOFILEPATH + foundOrder.Id + "\\lock.txt" );
                    }
                } );
            }
            catch( Exception e )
            {
                log.LogError( Event.ERROR, e, $"{this}.ValizoFilesReady(): ERROR! An internal server error occured while attempting to process files for order id '{orderId}'" );
                throw new WampException( new Dictionary<string, object>(), "erp.errors.internalservererror", $"ERROR! An internal server error occured while attempting to process files", null );
            }

            log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.ValizoFilesReady() finished" );
        }

        [WampProcedure( "erp.valizo.order.getprice" )]
        public async Task<int> ValizoPriceGet( bool departure, string flightNumber, bool privateAddress, int luggageAmount, int oddsizeAmount, string zipSelectionId = "", string hotelSelectionId = "", int personAmount = 6, int bookingAmount = 1, int plasticBagAmount = 0, int airshellAmount = 0, bool returnService = false )
        {
            log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.ValizoPriceGet() entered" );

            //Initial price for both services
            var price = 395;
            //if( departure )
            {
                //Check if the given flightnumber exists
                //If it doesn't, this is either arrival service or the simple departure service
                var flightInitials = flightNumber.Length > 2 ? flightNumber.Substring( 0, 2 ) : flightNumber;
                Airline airline = null;
                if( !string.IsNullOrEmpty( flightInitials ) )
                    airline = await ( await Database.GetAsync<Airline>() ).Query.FirstOrDefaultAsync( x => x.FlightInitials == flightInitials );
                //if( airline == null )
                {
                    //Simple
                    //Check if this is a hotel pickup or not
                    if( privateAddress )
                    {
                        //Look up the price in the zip table then
                        var foundSelection = await ( await Database.GetAsync<ZipPriceTable>() ).Query.FirstOrDefaultAsync( x => x.Id == zipSelectionId );

                        //If zip not found, assume price is 395
                        price = foundSelection == null ? 395 : foundSelection.Price;

                        //If this is a departure service and it is the simple service, subtract 50 from the price
                        if( departure && airline == null )
                            price -= 50;

                        if( departure && returnService )
                            price += foundSelection == null ? 295 : foundSelection.Price - 100;
                    }
                    else
                    {
                        //Look up the hotel in the hotel table then
                        var foundHotelSelection = await ( await Database.GetAsync<HotelPriceTable>() ).Query.FirstOrDefaultAsync( x => x.Id == hotelSelectionId );

                        //If none found, we base the price on the given zip code for the hotel instead
                        if( foundHotelSelection == null )
                        {
                            //Look up the price in the zip table then
                            var foundZipSelection = await ( await Database.GetAsync<ZipPriceTable>() ).Query.FirstOrDefaultAsync( x => x.Id == zipSelectionId );

                            //If this is a departure and it is the simple service, subtract 200 from the price
                            if( departure && airline == null )
                                price = foundZipSelection == null ? 195 : foundZipSelection.Price - 200;
                            else
                                price = foundZipSelection == null ? 395 : foundZipSelection.Price;
                        }
                        else
                            price = foundHotelSelection.Price;
                    }
                }
                /*else
                {
                    //Advanced


                }*/
            }
            /*else
            {
                //price += 
            }*/

            if( !privateAddress )
            {
                price += 30 * ( luggageAmount - 6 ).Clamp( 0, luggageAmount );
                price += 45 * oddsizeAmount;
            }
            else
            {
                price += 45 * ( luggageAmount - 3 ).Clamp( 0, luggageAmount );
                price += 45 * oddsizeAmount;
            }

            price += 50 * ( personAmount - 6 ).Clamp( 0, personAmount );

            price += 100 * ( bookingAmount - 1 ).Clamp( 0, bookingAmount );

            price += 45 * plasticBagAmount;

            price += 45 * airshellAmount;

            log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.ValizoPriceGet() finished" );

            return price;
        }

        private static ProcessingSettings buildSettings( string language,
            string outputFormat, string profile )
        {
            ProcessingSettings settings = new ProcessingSettings();
            settings.SetLanguage( language );
            switch( outputFormat.ToLower() )
            {
                case "txt":
                    settings.SetOutputFormat( OutputFormat.txt );
                    break;
                case "rtf":
                    settings.SetOutputFormat( OutputFormat.rtf );
                    break;
                case "docx":
                    settings.SetOutputFormat( OutputFormat.docx );
                    break;
                case "xlsx":
                    settings.SetOutputFormat( OutputFormat.xlsx );
                    break;
                case "pptx":
                    settings.SetOutputFormat( OutputFormat.pptx );
                    break;
                case "pdfsearchable":
                    settings.SetOutputFormat( OutputFormat.pdfSearchable );
                    break;
                case "pdftextandimages":
                    settings.SetOutputFormat( OutputFormat.pdfTextAndImages );
                    break;
                case "xml":
                    settings.SetOutputFormat( OutputFormat.xml );
                    break;
                default:
                    throw new ArgumentException( "Invalid output format" );
            }
            if( profile != null )
            {
                switch( profile.ToLower() )
                {
                    case "documentconversion":
                        settings.Profile = Profile.documentConversion;
                        break;
                    case "documentarchiving":
                        settings.Profile = Profile.documentArchiving;
                        break;
                    case "textextraction":
                        settings.Profile = Profile.textExtraction;
                        break;
                    default:
                        throw new ArgumentException( "Invalid profile" );
                }
            }

            return settings;
        }

        #endregion

        #region KK

        #region SIPMember

        [WampProcedure( "kk.sip.sipmember.create" )]
        public async Task<string> KKSIPMemberCreate( SIPMember sipMember )
        {
            try
            {
                log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.SIPMemberCreate() entered" );

                if( sipMember == null )
                    throw new WampException( "kk.sip.sipmember.error.invalidobject", "ERROR! The SIPMember object cannot be null" );
                if( !string.IsNullOrEmpty( sipMember.Id ) )
                    throw new WampException( "kk.sip.sipmember.error.invalidid", $"ERROR! Cannot add a new SIPMember with an already assigned id" );



                var rep = Database.Get<SIPMember>();

                await rep.InsertAsync( sipMember );

                if( sipMember.Teams != null && sipMember.Teams.Length > 0 )
                    await this.KKUpdateSIPMemberToTeams( sipMember.SparkEmail, sipMember.Teams );

                OnSIPMemberAdded.OnNext( sipMember );

                return sipMember.Id;
            }
            catch( WampException )
            {
                throw;
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, $"{this}.SIPMemberCreate(): failed to insert sipmember into database" );
                throw new WampException( "erp.error.internalservererror", "ERROR! An internal server error occured" );
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.SIPMemberCreate() finished" );
            }
        }

        [WampProcedure( "kk.sip.sipmember.getstate" )]
        public async Task<GetStateResult<SIPMember>> KKSIPMemberGetState( int first = 0, int count = 0, bool sortOrderAscending = true, string sortField = "Id", List<FilterData> filters = null, List<string> idList = null, bool includeList = true )
        {
            try
            {
                log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.SIPMemberGetState() entered" );

                return await GetObjectState<SIPMember>( first, count, sortOrderAscending, sortField, filters, idList: idList, includeList: includeList );
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, $"{this}.SIPMemberGetState(): failed to get state of sipmembers from database" );
                throw new WampException( "erp.error.internalservererror", "ERROR! An internal server error occured" );
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.SIPMemberGetState() finished" );
            }
        }

        [WampProcedure( "kk.sip.sipmember.update" )]
        public async Task KKSIPMemberUpdate( SIPMember sipMemberModifications, UpdateAction[] actions = null )
        {
            try
            {
                log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.SIPMemberUpdate() entered" );

                if( sipMemberModifications == null )
                    throw new WampException( "kk.sip.sipmember.error.invalidobject", "ERROR! SIPMember cannot be null" );
                if( string.IsNullOrEmpty( sipMemberModifications.Id ) )
                    throw new WampException( "kk.sip.sipmember.error.invalidid", "ERROR! Id of SIPMember cannot be null or empty" );

                var repo = await Database.GetAsync<SIPMember>();

                var found = await repo.Query.FirstOrDefaultAsync( x => x.Id == sipMemberModifications.Id );
                if( found == null )
                    throw new WampException( "kk.sip.sipmember.error.invalidid", $"ERROR! Failed to find sipmember with given id '{sipMemberModifications.Id}'" );

                await repo.UpdateAsync( sipMemberModifications, actions );

                found = await repo.Query.FirstOrDefaultAsync( x => x.Id == sipMemberModifications.Id );

                OnSIPMemberUpdated.OnNext( found );
            }
            catch( WampException )
            {
                throw;
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, $"{this}.SIPMemberUpdate(): failed to update sipmember in database" );
                throw new WampException( "erp.error.internalservererror", "ERROR! An internal server error occured" );
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.SIPMemberUpdate() finished" );
            }
        }

        [WampProcedure( "kk.sip.sipmember.replace" )]
        public async Task KKSIPMemberReplace( SIPMember sipMember )
        {
            try
            {
                log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.SIPMemberReplace() entered" );

                if( sipMember == null )
                    throw new WampException( "kk.sip.sipmember.error.invalidobject", "ERROR! SIPMember cannot be null" );
                if( string.IsNullOrEmpty( sipMember.Id ) )
                    throw new WampException( "kk.sip.sipmember.error.invalidid", "ERROR! Id of SIPMember cannot be null or empty" );

                var rep = Database.Get<SIPMember>();

                log.LogTrace( Event.VARIABLEGET, $"{this}.SIPMemberReplace(): looking for existing sipmember with id: '{sipMember.Id}'" );
                var found = await rep.Query.FirstOrDefaultAsync( x => x.Id == sipMember.Id );
                if( found == null )
                    throw new WampException( "kk.sip.sipmember.error.invalidid", $"ERROR! No sipmember with the id: '{ sipMember.Id }'" );

                await rep.ReplaceAsync( sipMember );

                OnSIPMemberUpdated.OnNext( sipMember );
            }
            catch( WampException )
            {
                throw;
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, $"{this}.SIPMemberReplace(): failed to replace sipmember in database" );
                throw new WampException( "erp.error.internalservererror", "ERROR! An internal server error occured" );
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.ProductBrandReplace() finished" );
            }
        }

        [WampProcedure( "kk.sip.sipmember.delete" )]
        public async Task KKSipMemberDelete( string id )
        {
            try
            {
                log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.SipMemberDelete() entered" );

                if( string.IsNullOrEmpty( id ) )
                    throw new WampException( "kk.sip.sipmember.error.invalidid", "id" );

                var rep = Database.Get<ProductBrand>();

                log.LogTrace( Event.VARIABLEGET, $"{this}.SipMemberDelete(): looking for existing sipmember with id: '{id}'" );
                var found = await rep.Query.FirstOrDefaultAsync( x => x.Id == id );
                if( found == null )
                    throw new WampException( "kk.sip.sipmember.error.invalidid", $"ERROR! No sipmember with the id: '{ id }' found" );

                await rep.DeleteAsync( id );

                OnSIPMemberDeleted.OnNext( id );
            }
            catch( WampException )
            {
                throw;
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, $"{this}.SipMemberDelete(): failed to delete sipmember in database" );
                throw new WampException( "erp.error.internalservererror", "ERROR! An internal server error occured" );
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.SipMemberDelete() finished" );
            }
        }

        #endregion

        #region CiscoSpark API

        [WampProcedure( "kk.sip.updatesipmemberteams" )]
        public async Task KKUpdateSIPMemberToTeams( string sparkEmail, string[] teams )
        {
            try
            {
                log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.SIPMemberCreate() entered" );

                if( string.IsNullOrEmpty( sparkEmail ) )
                    throw new WampException( "kk.sip.updatesipmembertoteams.error.invalidobject", "ERROR! The sparkEmail cannot be null or empty string" );
                /*if( teams == null || teams.Length == 0 )
                    throw new WampException( "kk.sip.updatesipmembertoteams.error.invalidobject", $"ERROR! The teams array cannot be null or empty" );*/

                var rep = Database.Get<SIPMember>();
                var result = await rep.Query.FirstOrDefaultAsync( x => x.SparkEmail == sparkEmail );
                if( result == null )
                    throw new WampException( "kk.sip.updatesipmembertoteams.error.invalidmemberemail", $"ERROR! A member with the email '{ sparkEmail }' doesn't exist" );

                var spark = new Spark( CISCOTOKEN );

                var person = await spark.GetPeopleAsync( sparkEmail );
                if( person.Count != 1 )
                    throw new WampException( "kk.sip.updatesipmembertoteams.error.invalidmemberemail", $"ERROR! The email of '{ sparkEmail }' didn't return a unique person" );

                /*var uniquePerson = await spark.GetPersonAsync( person[ 0 ].id );
                if( uniquePerson == null )
                    throw new WampException( "kk.sip.updatesipmembertoteams.error.invalidmember", $"ERROR! Failed to get Person from Cisco API" );*/

                var currentTeams = await this.KKGetTeamMembershipsForPerson( sparkEmail );

                //Leave removed ones
                foreach( var team in currentTeams )
                {
                    if( !teams.Contains( team ) )
                    {
                        var uniqueTeam = await spark.GetTeamAsync( team );
                        if( uniqueTeam == null )
                            throw new WampException( "kk.sip.updatesipmembertoteams.error.invalidteam", $"ERROR! The team id of '{ team }' doesn't exist" );

                        var memberships = await spark.GetTeamMembershipsAsync( team );
                        var uniqueMembership = memberships?.FirstOrDefault( x => x.personId == person[ 0 ].id );
                        if( uniqueMembership == null )
                            throw new WampException( "kk.sip.updatesipmembertoteams.error.invalidteammembership", $"ERROR! Couldn't find member in the membership list of team '{ team }'" );

                        if( !await spark.DeleteTeamMembershipAsync( uniqueMembership.id ) )
                            throw new WampException( "kk.sip.updatesipmembertoteams.error.failedtoremove", $"ERROR! Failed to remove member from the team '{ team }'" );
                    }
                }

                //Join new ones
                foreach( var team in teams )
                {
                    if( !currentTeams.Contains( team ) )
                    {
                        var uniqueTeam = await spark.GetTeamAsync( team );
                        if( uniqueTeam == null )
                            throw new WampException( "kk.sip.updatesipmembertoteams.error.invalidteam", $"ERROR! The team id of '{ team }' doesn't exist" );

                        if( ( await spark.CreateTeamMembershipAsync( team, personEmail: sparkEmail ) ) == null )
                            throw new WampException( "erp.error.internalservererror", $"ERROR! An internal server error occured" );
                    }
                }

                result.Teams = teams;

                await this.KKSIPMemberReplace( result );
            }
            catch( WampException )
            {
                //throw;
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, $"{this}.SIPMemberCreate(): failed to insert sipmember into database" );
                //throw new WampException( "erp.error.internalservererror", "ERROR! An internal server error occured" );
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.SIPMemberCreate() finished" );
            }
        }

        [WampProcedure( "kk.sip.getteammembershipsforperson" )]
        public async Task<string[]> KKGetTeamMembershipsForPerson( string sparkEmail )
        {
            try
            {
                log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.KKGetTeamMembershipsForPerson() entered" );

                var spark = new Spark( CISCOTOKEN );

                var person = await spark.GetPeopleAsync( sparkEmail );
                if( person.Count != 1 )
                    throw new WampException( "kk.sip.updatesipmembertoteams.error.invalidmemberemail", $"ERROR! The email of '{ sparkEmail }' didn't return a unique person" );

                var teams = new List<string>();
                foreach( var team in ( await this.KKGetTeams() ) )
                {
                    var memberships = await spark.GetTeamMembershipsAsync( team.id );
                    if( memberships?.FirstOrDefault( x => x.personId == person[ 0 ].id ) != null )
                        teams.Add( team.id );
                }

                return teams.ToArray();
            }
            catch( WampException )
            {
                throw;
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, $"{this}.KKGetTeamMembershipsForPerson(): failed to get team memberships from cisco" );
                throw new WampException( "erp.error.internalservererror", "ERROR! An internal server error occured" );
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.KKGetTeamMembershipsForPerson() finished" );
            }
        }

        [WampProcedure( "kk.sip.getteams" )]
        public async Task<Team[]> KKGetTeams()
        {
            try
            {
                log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.KKGetTeams() entered" );

                var spark = new Spark( CISCOTOKEN );

                return ( await spark.GetTeamsAsync() )?.ToArray();
            }
            catch( WampException )
            {
                throw;
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, $"{this}.KKGetTeams(): failed to get teams from cisco" );
                throw new WampException( "erp.error.internalservererror", "ERROR! An internal server error occured" );
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.KKGetTeams() finished" );
            }
        }

        [WampProcedure( "kk.sip.getpeople" )]
        public async Task<SparkDotNet.Person[]> KKGetPeople()
        {
            try
            {
                log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.KKGetPeople() entered" );

                var spark = new Spark( CISCOTOKEN );

                return ( await spark.GetPeopleAsync() )?.ToArray();
            }
            catch( WampException )
            {
                throw;
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, $"{this}.KKGetPeople(): failed to get people from cisco" );
                throw new WampException( "erp.error.internalservererror", "ERROR! An internal server error occured" );
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.KKGetPeople() finished" );
            }
        }

        #endregion

        [WampProcedure( "kk.sip.checksip" )]
        public async Task<string> KKCheckSIP( string ciscoNumber )
        {
            try
            {
                log.LogDebug( Event.ENTEREDFUNCTION, $"{this}.KKCheckSIP() entered" );

                if( string.IsNullOrEmpty( ciscoNumber ) )
                    throw new WampException( "kk.sip.checksip.error.invalidobject", "ERROR! ciscoNumber cannot be null or empty" );

                var rep = Database.Get<SIPMember>();

                var existingMember = await rep.Query.FirstOrDefaultAsync( x => x.SparkEmail == ciscoNumber );

                return existingMember == null ? "" : existingMember.SIPNumber;
            }
            catch( WampException )
            {
                throw;
            }
            catch( Exception e )
            {
                log.LogError( Event.EXCEPTIONCAUGHT, e, $"{this}.KKCheckSIP(): failed to find sip members in database" );
                throw new WampException( "erp.error.internalservererror", "ERROR! An internal server error occured" );
            }
            finally
            {
                log.LogDebug( Event.FINISHEDFUNCTION, $"{this}.KKCheckSIP() finished" );
            }
        }



        #endregion

        #region Debug

        [WampProcedure( "erp.debug.test" )]
        public async Task Test()
        {
            /*var sessionCount = await procedures.GetWampSessionCount();
            var list = await procedures.GetWampSessionList();
            var session = await procedures.GetWampSession( list[ 0 ] );*/

            /*var test = await this.AssortmentGetState( filters: new List<FilterData>() { new FilterData() { FieldName = "name", Value = "be9d8c", MatchMode = 1 } } );
            //var testRep = database.Get<Assortment>();
            //var first5 = await testRep.Query.Take( 5 ).ToAsyncEnumerable().ToList();
            var user = new User();*/
            #region JSON Definition
            var countryJson = @"[ 
  {name: 'Afghanistan', code: 'AF'}, 
  {name: 'Åland Islands', code: 'AX'}, 
  {name: 'Albania', code: 'AL'}, 
  {name: 'Algeria', code: 'DZ'}, 
  {name: 'American Samoa', code: 'AS'}, 
  {name: 'AndorrA', code: 'AD'}, 
  {name: 'Angola', code: 'AO'}, 
  {name: 'Anguilla', code: 'AI'}, 
  {name: 'Antarctica', code: 'AQ'}, 
  {name: 'Antigua and Barbuda', code: 'AG'}, 
  {name: 'Argentina', code: 'AR'}, 
  {name: 'Armenia', code: 'AM'}, 
  {name: 'Aruba', code: 'AW'}, 
  {name: 'Australia', code: 'AU'}, 
  {name: 'Austria', code: 'AT'}, 
  {name: 'Azerbaijan', code: 'AZ'}, 
  {name: 'Bahamas', code: 'BS'}, 
  {name: 'Bahrain', code: 'BH'}, 
  {name: 'Bangladesh', code: 'BD'}, 
  {name: 'Barbados', code: 'BB'}, 
  {name: 'Belarus', code: 'BY'}, 
  {name: 'Belgium', code: 'BE'}, 
  {name: 'Belize', code: 'BZ'}, 
  {name: 'Benin', code: 'BJ'}, 
  {name: 'Bermuda', code: 'BM'}, 
  {name: 'Bhutan', code: 'BT'}, 
  {name: 'Bolivia', code: 'BO'}, 
  {name: 'Bosnia and Herzegovina', code: 'BA'}, 
  {name: 'Botswana', code: 'BW'}, 
  {name: 'Bouvet Island', code: 'BV'}, 
  {name: 'Brazil', code: 'BR'}, 
  {name: 'British Indian Ocean Territory', code: 'IO'}, 
  {name: 'Brunei Darussalam', code: 'BN'}, 
  {name: 'Bulgaria', code: 'BG'}, 
  {name: 'Burkina Faso', code: 'BF'}, 
  {name: 'Burundi', code: 'BI'}, 
  {name: 'Cambodia', code: 'KH'}, 
  {name: 'Cameroon', code: 'CM'}, 
  {name: 'Canada', code: 'CA'}, 
  {name: 'Cape Verde', code: 'CV'}, 
  {name: 'Cayman Islands', code: 'KY'}, 
  {name: 'Central African Republic', code: 'CF'}, 
  {name: 'Chad', code: 'TD'}, 
  {name: 'Chile', code: 'CL'}, 
  {name: 'China', code: 'CN'}, 
  {name: 'Christmas Island', code: 'CX'}, 
  {name: 'Cocos (Keeling) Islands', code: 'CC'}, 
  {name: 'Colombia', code: 'CO'}, 
  {name: 'Comoros', code: 'KM'}, 
  {name: 'Congo', code: 'CG'}, 
  {name: 'Congo, The Democratic Republic of the', code: 'CD'}, 
  {name: 'Cook Islands', code: 'CK'}, 
  {name: 'Costa Rica', code: 'CR'}, 
  {name: 'Cote D\'Ivoire', code: 'CI'}, 
  {name: 'Croatia', code: 'HR'}, 
  {name: 'Cuba', code: 'CU'}, 
  {name: 'Cyprus', code: 'CY'}, 
  {name: 'Czech Republic', code: 'CZ'}, 
  {name: 'Denmark', code: 'DK'}, 
  {name: 'Djibouti', code: 'DJ'}, 
  {name: 'Dominica', code: 'DM'}, 
  {name: 'Dominican Republic', code: 'DO'}, 
  {name: 'Ecuador', code: 'EC'}, 
  {name: 'Egypt', code: 'EG'}, 
  {name: 'El Salvador', code: 'SV'}, 
  {name: 'Equatorial Guinea', code: 'GQ'}, 
  {name: 'Eritrea', code: 'ER'}, 
  {name: 'Estonia', code: 'EE'}, 
  {name: 'Ethiopia', code: 'ET'}, 
  {name: 'Falkland Islands (Malvinas)', code: 'FK'}, 
  {name: 'Faroe Islands', code: 'FO'}, 
  {name: 'Fiji', code: 'FJ'}, 
  {name: 'Finland', code: 'FI'}, 
  {name: 'France', code: 'FR'}, 
  {name: 'French Guiana', code: 'GF'}, 
  {name: 'French Polynesia', code: 'PF'}, 
  {name: 'French Southern Territories', code: 'TF'}, 
  {name: 'Gabon', code: 'GA'}, 
  {name: 'Gambia', code: 'GM'}, 
  {name: 'Georgia', code: 'GE'}, 
  {name: 'Germany', code: 'DE'}, 
  {name: 'Ghana', code: 'GH'}, 
  {name: 'Gibraltar', code: 'GI'}, 
  {name: 'Greece', code: 'GR'}, 
  {name: 'Greenland', code: 'GL'}, 
  {name: 'Grenada', code: 'GD'}, 
  {name: 'Guadeloupe', code: 'GP'}, 
  {name: 'Guam', code: 'GU'}, 
  {name: 'Guatemala', code: 'GT'}, 
  {name: 'Guernsey', code: 'GG'}, 
  {name: 'Guinea', code: 'GN'}, 
  {name: 'Guinea-Bissau', code: 'GW'}, 
  {name: 'Guyana', code: 'GY'}, 
  {name: 'Haiti', code: 'HT'}, 
  {name: 'Heard Island and Mcdonald Islands', code: 'HM'}, 
  {name: 'Holy See (Vatican City State)', code: 'VA'}, 
  {name: 'Honduras', code: 'HN'}, 
  {name: 'Hong Kong', code: 'HK'}, 
  {name: 'Hungary', code: 'HU'}, 
  {name: 'Iceland', code: 'IS'}, 
  {name: 'India', code: 'IN'}, 
  {name: 'Indonesia', code: 'ID'}, 
  {name: 'Iran, Islamic Republic Of', code: 'IR'}, 
  {name: 'Iraq', code: 'IQ'}, 
  {name: 'Ireland', code: 'IE'}, 
  {name: 'Isle of Man', code: 'IM'}, 
  {name: 'Israel', code: 'IL'}, 
  {name: 'Italy', code: 'IT'}, 
  {name: 'Jamaica', code: 'JM'}, 
  {name: 'Japan', code: 'JP'}, 
  {name: 'Jersey', code: 'JE'}, 
  {name: 'Jordan', code: 'JO'}, 
  {name: 'Kazakhstan', code: 'KZ'}, 
  {name: 'Kenya', code: 'KE'}, 
  {name: 'Kiribati', code: 'KI'}, 
  {name: 'Korea, Democratic People\'S Republic of', code: 'KP'}, 
  {name: 'Korea, Republic of', code: 'KR'}, 
  {name: 'Kuwait', code: 'KW'}, 
  {name: 'Kyrgyzstan', code: 'KG'}, 
  {name: 'Lao People\'S Democratic Republic', code: 'LA'}, 
  {name: 'Latvia', code: 'LV'}, 
  {name: 'Lebanon', code: 'LB'}, 
  {name: 'Lesotho', code: 'LS'}, 
  {name: 'Liberia', code: 'LR'}, 
  {name: 'Libyan Arab Jamahiriya', code: 'LY'}, 
  {name: 'Liechtenstein', code: 'LI'}, 
  {name: 'Lithuania', code: 'LT'}, 
  {name: 'Luxembourg', code: 'LU'}, 
  {name: 'Macao', code: 'MO'}, 
  {name: 'Macedonia, The Former Yugoslav Republic of', code: 'MK'}, 
  {name: 'Madagascar', code: 'MG'}, 
  {name: 'Malawi', code: 'MW'}, 
  {name: 'Malaysia', code: 'MY'}, 
  {name: 'Maldives', code: 'MV'}, 
  {name: 'Mali', code: 'ML'}, 
  {name: 'Malta', code: 'MT'}, 
  {name: 'Marshall Islands', code: 'MH'}, 
  {name: 'Martinique', code: 'MQ'}, 
  {name: 'Mauritania', code: 'MR'}, 
  {name: 'Mauritius', code: 'MU'}, 
  {name: 'Mayotte', code: 'YT'}, 
  {name: 'Mexico', code: 'MX'}, 
  {name: 'Micronesia, Federated States of', code: 'FM'}, 
  {name: 'Moldova, Republic of', code: 'MD'}, 
  {name: 'Monaco', code: 'MC'}, 
  {name: 'Mongolia', code: 'MN'}, 
  {name: 'Montserrat', code: 'MS'}, 
  {name: 'Morocco', code: 'MA'}, 
  {name: 'Mozambique', code: 'MZ'}, 
  {name: 'Myanmar', code: 'MM'}, 
  {name: 'Namibia', code: 'NA'}, 
  {name: 'Nauru', code: 'NR'}, 
  {name: 'Nepal', code: 'NP'}, 
  {name: 'Netherlands', code: 'NL'}, 
  {name: 'Netherlands Antilles', code: 'AN'}, 
  {name: 'New Caledonia', code: 'NC'}, 
  {name: 'New Zealand', code: 'NZ'}, 
  {name: 'Nicaragua', code: 'NI'}, 
  {name: 'Niger', code: 'NE'}, 
  {name: 'Nigeria', code: 'NG'}, 
  {name: 'Niue', code: 'NU'}, 
  {name: 'Norfolk Island', code: 'NF'}, 
  {name: 'Northern Mariana Islands', code: 'MP'}, 
  {name: 'Norway', code: 'NO'}, 
  {name: 'Oman', code: 'OM'}, 
  {name: 'Pakistan', code: 'PK'}, 
  {name: 'Palau', code: 'PW'}, 
  {name: 'Palestinian Territory, Occupied', code: 'PS'}, 
  {name: 'Panama', code: 'PA'}, 
  {name: 'Papua New Guinea', code: 'PG'}, 
  {name: 'Paraguay', code: 'PY'}, 
  {name: 'Peru', code: 'PE'}, 
  {name: 'Philippines', code: 'PH'}, 
  {name: 'Pitcairn', code: 'PN'}, 
  {name: 'Poland', code: 'PL'}, 
  {name: 'Portugal', code: 'PT'}, 
  {name: 'Puerto Rico', code: 'PR'}, 
  {name: 'Qatar', code: 'QA'}, 
  {name: 'Reunion', code: 'RE'}, 
  {name: 'Romania', code: 'RO'}, 
  {name: 'Russian Federation', code: 'RU'}, 
  {name: 'RWANDA', code: 'RW'}, 
  {name: 'Saint Helena', code: 'SH'}, 
  {name: 'Saint Kitts and Nevis', code: 'KN'}, 
  {name: 'Saint Lucia', code: 'LC'}, 
  {name: 'Saint Pierre and Miquelon', code: 'PM'}, 
  {name: 'Saint Vincent and the Grenadines', code: 'VC'}, 
  {name: 'Samoa', code: 'WS'}, 
  {name: 'San Marino', code: 'SM'}, 
  {name: 'Sao Tome and Principe', code: 'ST'}, 
  {name: 'Saudi Arabia', code: 'SA'}, 
  {name: 'Senegal', code: 'SN'}, 
  {name: 'Serbia and Montenegro', code: 'CS'}, 
  {name: 'Seychelles', code: 'SC'}, 
  {name: 'Sierra Leone', code: 'SL'}, 
  {name: 'Singapore', code: 'SG'}, 
  {name: 'Slovakia', code: 'SK'}, 
  {name: 'Slovenia', code: 'SI'}, 
  {name: 'Solomon Islands', code: 'SB'}, 
  {name: 'Somalia', code: 'SO'}, 
  {name: 'South Africa', code: 'ZA'}, 
  {name: 'South Georgia and the South Sandwich Islands', code: 'GS'}, 
  {name: 'Spain', code: 'ES'}, 
  {name: 'Sri Lanka', code: 'LK'}, 
  {name: 'Sudan', code: 'SD'}, 
  {name: 'Suriname', code: 'SR'}, 
  {name: 'Svalbard and Jan Mayen', code: 'SJ'}, 
  {name: 'Swaziland', code: 'SZ'}, 
  {name: 'Sweden', code: 'SE'}, 
  {name: 'Switzerland', code: 'CH'}, 
  {name: 'Syrian Arab Republic', code: 'SY'}, 
  {name: 'Taiwan, Province of China', code: 'TW'}, 
  {name: 'Tajikistan', code: 'TJ'}, 
  {name: 'Tanzania, United Republic of', code: 'TZ'}, 
  {name: 'Thailand', code: 'TH'}, 
  {name: 'Timor-Leste', code: 'TL'}, 
  {name: 'Togo', code: 'TG'}, 
  {name: 'Tokelau', code: 'TK'}, 
  {name: 'Tonga', code: 'TO'}, 
  {name: 'Trinidad and Tobago', code: 'TT'}, 
  {name: 'Tunisia', code: 'TN'}, 
  {name: 'Turkey', code: 'TR'}, 
  {name: 'Turkmenistan', code: 'TM'}, 
  {name: 'Turks and Caicos Islands', code: 'TC'}, 
  {name: 'Tuvalu', code: 'TV'}, 
  {name: 'Uganda', code: 'UG'}, 
  {name: 'Ukraine', code: 'UA'}, 
  {name: 'United Arab Emirates', code: 'AE'}, 
  {name: 'United Kingdom', code: 'GB'}, 
  {name: 'United States', code: 'US'}, 
  {name: 'United States Minor Outlying Islands', code: 'UM'}, 
  {name: 'Uruguay', code: 'UY'}, 
  {name: 'Uzbekistan', code: 'UZ'}, 
  {name: 'Vanuatu', code: 'VU'}, 
  {name: 'Venezuela', code: 'VE'}, 
  {name: 'Viet Nam', code: 'VN'}, 
  {name: 'Virgin Islands, British', code: 'VG'}, 
  {name: 'Virgin Islands, U.S.', code: 'VI'}, 
  {name: 'Wallis and Futuna', code: 'WF'}, 
  {name: 'Western Sahara', code: 'EH'}, 
  {name: 'Yemen', code: 'YE'}, 
  {name: 'Zambia', code: 'ZM'}, 
  {name: 'Zimbabwe', code: 'ZW'} 
]";
            #endregion
            //Create country table:
            /*var countries = new List<Country>();

            var definition = new[] { new { name = "", code = "" } }.ToList();
            var deserialisedCountries = Newtonsoft.Json.JsonConvert.DeserializeAnonymousType( countryJson, definition );

            int i = 1;

            foreach( var x in deserialisedCountries )
            {
                await CountryCreate( new Country() { ISOCode = x.code, Name = x.name } );
            }*/

            //Create 100000 products:
            /*var products = new List<Product>( 1000 );
            var db = Database.Get<Product>();
            for( int i = 0; i < 1000; i++ )
            {
                products.Add( (Product) ReflectionUtils.InstantiateTypeWithRandomData( typeof( Product ) ) );
            }
            db.InsertMany( products.ToArray() );*/

            //OnUploadProgress.OnNext( "test" );

            /*var zipList = new List<int>() { 1050, 1051, 1052, 1053, 1054, 1055, 1055, 1056, 1057, 1058, 1059, 1060, 1061, 1062, 1063, 1064, 1065, 1066, 1067, 1068, 1069, 1070, 1071, 1072, 1073, 1074, 1100, 1101, 1102, 1103, 1104, 1105, 1106, 1107, 1110, 1111, 1112, 1113, 1114, 1115, 1116, 1117, 1118, 1119, 1120, 1121, 1122, 1123, 1124, 1125, 1126, 1127, 1128, 1129, 1130, 1131, 1140, 1147, 1148, 1150, 1151, 1152, 1153, 1154, 1155, 1156, 1157, 1158, 1159, 1160, 1161, 1162, 1163, 1164, 1165, 1166, 1167, 1168, 1169, 1170, 1171, 1172, 1173, 1174, 1175, 1200, 1201, 1202, 1203, 1204, 1205, 1206, 1207, 1208, 1209, 1210, 1211, 1212, 1213, 1214, 1215, 1216, 1217, 1218, 1218, 1218, 1218, 1218, 1218, 1219, 1220, 1221, 1240, 1250, 1251, 1252, 1253, 1254, 1255, 1256, 1257, 1258, 1259, 1259, 1260, 1261, 1263, 1263, 1264, 1265, 1266, 1267, 1268, 1270, 1271, 1291, 1300, 1301, 1302, 1303, 1304, 1306, 1307, 1307, 1308, 1309, 1310, 1311, 1312, 1313, 1314, 1315, 1316, 1317, 1318, 1319, 1320, 1321, 1322, 1323, 1324, 1325, 1326, 1327, 1328, 1329, 1349, 1350, 1352, 1353, 1354, 1355, 1356, 1357, 1358, 1359, 1360, 1361, 1361, 1362, 1363, 1364, 1365, 1366, 1367, 1368, 1369, 1370, 1371, 1400, 1400, 1401, 1402, 1402, 1402, 1402, 1402, 1403, 1404, 1405, 1406, 1407, 1408, 1409, 1410, 1411, 1411, 1412, 1413, 1414, 1415, 1416, 1417, 1418, 1419, 1420, 1421, 1422, 1423, 1424, 1425, 1426, 1427, 1428, 1429, 1430, 1431, 1432, 1432, 1432, 1433, 1433, 1433, 1433, 1433, 1433, 1433, 1434, 1435, 1436, 1436, 1436, 1436, 1436, 1436, 1436, 1437, 1437, 1437, 1437, 1437, 1437, 1437, 1437, 1437, 1437, 1437, 1437, 1437, 1437, 1438, 1438, 1438, 1438, 1438, 1438, 1439, 1439, 1439, 1439, 1439, 1439, 1439, 1439, 1439, 1439, 1439, 1439, 1440, 1440, 1440, 1440, 1440, 1440, 1440, 1440, 1440, 1440, 1440, 1441, 1441, 1441, 1448, 1450, 1451, 1452, 1453, 1454, 1455, 1456, 1457, 1458, 1459, 1460, 1461, 1462, 1463, 1464, 1465, 1466, 1467, 1468, 1470, 1471, 1472, 1473, 1500, 1501, 1502, 1503, 1504, 1505, 1506, 1507, 1508, 1509, 1510, 1532, 1533, 1550, 1550, 1551, 1552, 1553, 1553, 1554, 1555, 1556, 1557, 1558, 1559, 1560, 1561, 1561, 1562, 1563, 1564, 1566, 1567, 1568, 1569, 1570, 1570, 1571, 1572, 1573, 1574, 1575, 1576, 1577, 1592, 1599, 1600, 1601, 1602, 1603, 1604, 1605, 1606, 1607, 1608, 1609, 1610, 1611, 1612, 1613, 1614, 1615, 1616, 1617, 1618, 1619, 1620, 1620, 1621, 1622, 1623, 1624, 1630, 1631, 1632, 1633, 1634, 1635, 1640, 1650, 1651, 1652, 1653, 1654, 1655, 1656, 1657, 1658, 1659, 1660, 1660, 1661, 1662, 1663, 1664, 1665, 1666, 1667, 1668, 1669, 1670, 1671, 1671, 1672, 1673, 1674, 1675, 1676, 1677, 1699, 1700, 1701, 1702, 1703, 1704, 1705, 1706, 1707, 1708, 1709, 1710, 1711, 1712, 1713, 1714, 1715, 1716, 1717, 1718, 1719, 1720, 1721, 1722, 1723, 1724, 1725, 1726, 1727, 1728, 1729, 1730, 1731, 1732, 1733, 1734, 1735, 1736, 1737, 1738, 1739, 1748, 1749, 1750, 1751, 1752, 1753, 1754, 1755, 1756, 1757, 1758, 1759, 1760, 1761, 1762, 1763, 1764, 1765, 1766, 1770, 1771, 1772, 1773, 1774, 1775, 1777, 1778, 1780, 1782, 1784, 1785, 1786, 1787, 1789, 1790, 1795, 1799, 1800, 1801, 1802, 1803, 1804, 1805, 1806, 1807, 1808, 1809, 1810, 1811, 1812, 1813, 1814, 1815, 1816, 1817, 1818, 1819, 1820, 1822, 1823, 1824, 1825, 1826, 1827, 1828, 1829, 1835, 1850, 1851, 1852, 1853, 1854, 1855, 1856, 1857, 1860, 1861, 1862, 1863, 1864, 1865, 1866, 1867, 1868, 1870, 1871, 1872, 1873, 1874, 1875, 1876, 1877, 1878, 1879, 1900, 1901, 1902, 1903, 1904, 1905, 1906, 1908, 1909, 1910, 1911, 1912, 1913, 1914, 1915, 1916, 1917, 1920, 1921, 1922, 1923, 1924, 1925, 1926, 1927, 1928, 1931, 1950, 1951, 1952, 1953, 1954, 1955, 1956, 1957, 1958, 1959, 1960, 1961, 1962, 1963, 1964, 1965, 1966, 1967, 1970, 1971, 1972, 1973, 1974, 1999, 2791, 2000, 2100, 2200, 2300, 2400, 2450, 2500, 2600, 2605, 2610, 2620, 2625, 2630, 2635, 2640, 2650, 2660, 2665, 2670, 2680, 2690, 2700, 2720, 2730, 2740, 2750, 2760, 2765, 2770, 2800, 2820, 2830, 2840, 2850, 2860, 2870, 2880, 2900, 2920, 2930, 2942, 2950, 2960, 2970, 2980, 2990, 3000, 3050, 3060, 3070, 3450, 3460, 3500, 3520, 3540, 3660, 3670, 4000, 4030, 4600, 4621, 4622, 4623, 3080, 3330, 3400, 3480, 3490, 3550, 3600, 3650, 4040 };
            var priceList = new List<int>() { 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 395, 495, 495, 495, 495, 495, 495, 495, 495, 495, 495, 495, 495, 495, 495, 495, 495, 495, 545, 545, 545, 545, 545, 545, 545, 545, 545, 545, 545, 545, 545, 545, 545, 545, 545, 595, 595, 595, 595, 595, 595, 595, 595, 595 };

            var rep = await database.GetAsync<PriceTable>();

            await rep.InsertManyAsync( zipList.Zip( priceList, ( zip, price ) => new PriceTable() { Value = zip.ToString(), Price = price } ).ToArray() );*/

            //var jsonDefinition = "{ 'lul': { 'lul2': [ 'a', 'b', 'c' ] }, 'test': { 'yup': ['d', 'e', 'f'] } }";
            var jsonDefinition = @"{
  'Boligtilbehør': {
                'Gaveartikler': ['Div. gaveartikler','Legetøj','Sparegrise'],
    'Rengøring':  ['Afaldsstativer','Baljer','Børster','Div. rengøringsartikler','Fejebakke','Fnugfjerner','Glasskraber','Gulvskrubbe','Handsker','Klemmer','Klude','Koste','Mopper','Opvaskebakke','Opvaskestativ','Poser','Poser til støvsugere','Rengøringsmidler','Skafter','Snor','Spande','Strygebræt','Strygejern','Støvsugere','Svampe','Tæppebankere','Tæppefejere','Tøjopbevaring','Tørrestativer','Vandforstøvere','Vinduesvaskere','Vinduesskraber','Viskestykker'],
    'Indretning' : ['Bestikbakker','Blomster','Bøjler','Figurer','Foldeborde','Garderobestativer','Hyldepapir','Kasser','Klapstole','Kurve','Lanterner','Magneter','Måtter','Puder','Rammer','Skind','Skobakker','Skoreoler','Skuffemoduler','Taburetter','Termometre','Tæpper' ],
    'Højtider': ['Halloween','Jul','Påske'],
    'Gør Det Selv':  ['Gør','det','selv'],
    'Belysning og elartikler':  ['Baldakiner','Batterier','Batterilader','Bordlamper','Dekorationsbelysning','Fatninger','Gulvlamper','Havelamper','Julebelysning','Lampeskærme','Ledninger','Loftslamper','Lyskæder','Lystræer','Parkeringsur','Pære','Tænd/sluk ure','Væger til olielamper','Væglamper'],
    'Badeartikler':  ['Badeforhæng','Bademåtter','Badskrabere','Brusere','Div. Badeartikler','Hylder','Håndklæder','Kroge','Pedalspande','Propper','Sæbeholdere','Sæbepumper','Tandkrus','Toiletbørster','Vandsparer']
    },
  'Alt til bordet': {
    'Glas': ['Cafeglas','Champagneglas','Champagneskål','Cocktailglas','Cognacglas','Drinksglas','Hedvinsglas','Hvidvinsglas','Irish Coffee Glas','Portvinsglas','Punch sæt','Rumglas','Rødvinsglas','Shots glas','Snapseglas','Termoglas','Vandglas','Whiskyglas','Ølglas','Ølkrus'],
    'Stel': ['Fade','Kander','Kopper','Krus','Ovnfast','Skåle','Stegeso','Stel sæt','Tallerkner','Æggebægre'],
    'Bestik': ['Bestiksæt','Gaffeler','Kagespade','Knive','Salatsæt','Skeer'],
    'Borddækning': ['Askebægre','Bordskåner','Brødkurv','Dekoration','Div. Borddækning','Dug','Dækkeservietter','Flag','Lys','Lysestager','Lysmanchet','Salt og peber','Serveringsbakke','Servietter','Serviettholder','Termoskåle','Vaser']
}, 
  'Køkkenudstyr': {
    'Redskaber': ['Bøfformer','Citrus presser','Creme bule brænder','Dørsalg','Dåsepresser','Dåseåbner','Forklæde','Friturekurv','Fyrfadsvarmer','Grillhandske','Grydelapper','Grydeske','Hulske','Hvidløgs presser','Is ske','Julienna jern','Kartoffelmoser','Knivmagnet','Knivsliber','Kærnehusudstikker','Kødhakker','Kødhammer','Køkkenknive','Køkkenrulleholder','Lighter','Lågåbner','Madpincet','Mandulinjern','Minutur','Morter','Målebægre','Nøddeknækker','Osteskærer','Palet','Pasta','Persillehakker','Piskeris','Pizzahjul','Ramakiner','Rivejern','Saftstativ','Sakse','Salatslynge','Sigte','Skræller','Skærebræt','Stegepipette','Stegetermometer','Suppeøse','Tragte','Æbledeler','Æggedeler'],
    'Opbevaring':  ['Bokse','Brødkasse','Drikkedunke','Dåser','Geleglas','Henkogningsglas','Krydderiglas','Madkasser','Olie/eddike','Opbevaringskrukker','Patentflasker','Saftbeholder'],
    'Kaffe/te': ['Div. Kaffe/te','Elkedel','Espressokande','Kaffe/te dåser','Kaffebrygger','Kaffekværn','Kaffemaskiner','Kaffetragt','Mælkeskummer','Skål til tepose','Stempelkande ','Te brygger','Tekande','Termoflaske','Termokande','Termokrus','Te-æg'],
    'Gryder': ['Alm. Gryde','Fonduegryde','Grydesæt','Kasserolle','Låg','Saftkoger','Stegegryde','Suppegryde'],
    'Pander': ['Blinispande','Grillpande','Pandekagepande','Sauterpande','Stegepande','Wok','Æbleskivepande'],
    'Bage artikler': ['Bage ark','Bageforme','Bage tilbehør','Bradepande','Dejskraber','Flødesifon','Kagefade','Kageringe','Kageruller','Kantfolie','Klejnespore','Køkkenvægte','Låg til røre skåle','Målebægre','Måleskeer','Pensler','Pizzasten','Røreskåle','Sprøjteposer','Udstikker'],
    'Vin tilbehør': ['Div. Vintilbehør','Isspande','Karafler','Oplukkere','Proptrækkere','Shakere','Vinkølere','Vinpropper','Vinreoler','Whiskysten'],
    'El-apparater': ['Blendere','Bordgrill','Brødristere','Chokoladesmeltere','Div. elapparater','Dørklokker','Elektrisk kødhakkere','Friture','Hovedtelefoner','Håndmixere','Juicere','Kogeplader','Kogeplader','Køkkenmaskiner','Mikroovne','Minihakker','Mobiltelefon tilbehør','Ovne','Pålægsmaskiner','Raclette','Radioer','Røgalarmer','Smoothie blender','Sodavandsmaskiner','Toastere','Ure','Vaffeljern','Vaffeljern','Vakuumpakker','Varmeapparater','Vejrstationer','Ventilatorer','Æbleskivebager','Æggekoger']
  },
  'Haven': {
    'Havemøbler': ['Borde','Bænke','Div. Havemøbler','Dugeklemmer','Hynder','Hængekøjer','Overdækning','Parasolbeslag','Parasolfod','Parasoller','Stole'],
    'Dekoration': ['Altankasser','Bindetråd','Blomsterpinde','Foderbræt','Fugle','Krukker'],
    'Redskaber': ['Div. Redskaber', 'Fliserensere','Gribetænger','Handsker','Havesakse','Insekt bekæmpelse','Planteredskaber','Regnmålere','Vandkander','Vandslanger'],
    'Grill': ['Grill tilbehør','Transportabel grill']
  },
  'Fritid': {
    'Cykel': ['Cykelkurve','Cykellåselåse','Cykelpumper','Cykellygter'],
    'Rejse': ['Festivalstole','Gasblus','Indkøbsvogne','Kuffertvægt','Kuffetere','Køleelementer','Køletasker','Liggeunderlag','Luftmadrasser','Lygter','Nakkepude','Paraplyer','Rejseadapter','Soveposer','Tasker','Telte','Vanddunke']
  },
  'Personligpleje': {
    'Hår': ['Epilator','Fladjern','Hårklippere','Hårtørrer','Krøllejern'],
    'Velvære': ['Blodtryksmåler','Briller','Fodfile','Massage','Neglefile','Person termometer','Personvægte','Skohorn','Spejle','Sutsko','Tandbørster','Varmedunke','Varmepuder','Varmetæpper'],
    'Skæg': ['Shavere','Trimmer']
  }
}";


            var reader = new Newtonsoft.Json.JsonTextReader( new StringReader( jsonDefinition ) );
            var stack = new Stack<string>();
            while( reader.Read() )
            {
                switch( reader.TokenType )
                {
                    case Newtonsoft.Json.JsonToken.StartObject:
                        reader.Read();
                        var name = (string) reader.Value;
                        stack.Push( await CategoryCreate( new Category() { Name = name, ParentCategory = ( stack.Count > 0 ? new DBRef( stack.Peek(), "", "" ) : null ) } ) );
                        break;
                    case Newtonsoft.Json.JsonToken.StartArray:
                        while( reader.Read() && reader.TokenType == Newtonsoft.Json.JsonToken.String )
                            await CategoryCreate( new Category() { Name = (string) reader.Value, ParentCategory = new DBRef( stack.Peek(), "", "" ) } );
                        stack.Pop();
                        break;
                    case Newtonsoft.Json.JsonToken.PropertyName:
                        stack.Push( await CategoryCreate( new Category() { Name = (string) reader.Value, ParentCategory = ( stack.Count > 0 ? new DBRef( stack.Peek(), "", "" ) : null ) } ) );
                        break;
                    case Newtonsoft.Json.JsonToken.EndObject:
                        //case Newtonsoft.Json.JsonToken.EndArray:
                        if( stack.Count > 0 )
                            stack.Pop();
                        break;
                    default:
                        break;
                }
            }
        }

        [WampProcedure( "erp.debug.getmodellist" )]
        public async Task<string> DebugGetModelList( bool valizoOnly = false, bool githubWiki = true )
        {
            var dict = Assembly.GetEntryAssembly().GetTypes().Where(
                        x =>
                        {
                            var typeInfo = x.GetTypeInfo();
                            if( typeInfo == null || !typeInfo.IsClass || !typeInfo.IsPublic )
                                return false;
                            var attr = typeInfo.GetCustomAttribute<KTDocumentationAttribute>();
                            //TODO: Fix string comparison to pass the turkey test
                            return ( valizoOnly ? x.Namespace.StartsWith( "KTBackend.Models.Valizo" )
                                        || x == typeof( Address )
                                        || x == typeof( DBRef ) : x.Namespace.StartsWith( "KTBackend.Models" )
                                    ) && ( attr == null || !attr.Ignore );
                        } ).Select( x => new
                        {
                            Key = x.Namespace.Substring( x.Namespace.IndexOf( '.' ) + 1 ),
                            Value = x
                        } );

            var sb = new StringBuilder();

            sb.AppendLine( "KTBackend models as of " + DateTime.Now + ( valizoOnly ? " for Valizo" : "" ) );
            sb.AppendLine();

            foreach( var kvp in dict )
            {
                sb.AppendLine( $"- [{kvp.Value.Name}](#{kvp.Value.Name.ToLower()})" );
            }

            sb.AppendLine();

            foreach( var group in dict.OrderBy( x => x.Key ).GroupBy( x =>
            {
                var firstDotIndex = x.Key.IndexOf( '.' );
                if( firstDotIndex < 0 )
                    return x.Key;
                else
                    return x.Key.Substring( firstDotIndex + 1 );
            } ) )
            {
                foreach( var kvp in group )
                {
                    var type = kvp.Value;
                    if( githubWiki )
                    {
                        sb.AppendLine( "# " + type.Name );
                        sb.AppendLine();
                        sb.AppendLine( valizoOnly ? " Navn | Påkrævet | Beskrivelse | Standard værdi | Eksempel " : " Name | Required | Description | Default value | Example " );
                        sb.AppendLine( " --- | --- | --- | --- | --- " );
                        foreach( var member in type.GetFields( BindingFlags.FlattenHierarchy | BindingFlags.Public | BindingFlags.Instance ).Cast<MemberInfo>().Concat( type.GetProperties( BindingFlags.FlattenHierarchy | BindingFlags.Public | BindingFlags.Instance ) ).OrderBy( x => x.Name ) )
                        {
                            KTDocumentationAttribute attr = member.GetCustomAttribute<KTDocumentationAttribute>( true );
                            if( attr == null )
                                continue;
                            sb.AppendLine( $" {member.Name} | { ( attr.Required ? ( valizoOnly ? "Ja" : "Yes" ) : ( valizoOnly ? "Nej" : "No" ) ) } | { ( valizoOnly ? attr.DescriptionDanish : attr.DescriptionEnglish ) } | { attr.DefaultValue ?? "" } | { attr.Example ?? "" } " );
                        }
                        sb.AppendLine();
                        sb.AppendLine();
                    }
                    else
                    {

                    }
                }


            }

            var result = sb.ToString();

            result = result.Replace( "`1", "" );
            result = result.Replace( "`2", "" );
            result = result.Replace( "`3", "" );

#if DEBUG
            File.WriteAllText( Directory.GetCurrentDirectory() + "\\models.txt", result );
#endif

            return result;
        }

        [WampProcedure( "erp.debug.getprocedurelist" )]
        public async Task<string> DebugGetProcedureList( bool valizoOnly = false, bool formatSpaces = false, bool githubWiki = true )
        {
            var dict = new Dictionary<string, MethodInfo>();
            foreach( var method in this.GetType().GetMethods( BindingFlags.Public | BindingFlags.Instance ).Where( x => x.IsDefined( typeof( WampProcedureAttribute ) ) ) )
            {
                var attr = method.GetCustomAttribute<WampProcedureAttribute>();
                if( valizoOnly && ( !attr.Procedure.StartsWith( "erp.valizo" ) || attr.Procedure == "erp.valizo.order.filesready" || attr.Procedure == "erp.valizo.order.getprice" ) )
                    continue;
                dict.Add( attr.Procedure, method );
            }

            var sb = new StringBuilder();

            sb.AppendLine( "KTBackend procedures as of " + DateTime.Now + ( valizoOnly ? " for Valizo" : "" ) );
            sb.AppendLine();

            foreach( var item in dict.OrderBy( x => x.Key ).GroupBy( x =>
            {
                var firstDotIndex = x.Key.IndexOf( '.' );
                var secondDotIndex = x.Key.Substring( firstDotIndex + 1 ).IndexOf( '.' );
                if( valizoOnly )
                {
                    var dotIndex = x.Key.Substring( firstDotIndex + secondDotIndex + 1 + 1 ).IndexOf( '.' );
                    return x.Key.Substring( firstDotIndex + 1 + secondDotIndex + 1, dotIndex );
                }

                return x.Key.Substring( firstDotIndex + 1, secondDotIndex );
            } ) )
            {
                //Title
                if( githubWiki )
                {
                    sb.AppendLine( "# " + item.Key );
                    sb.AppendLine();
                    sb.AppendLine( valizoOnly ? " Returtype | Procedurenavn | Parametre " : " Return type | Procedure name | Parameters " );
                    sb.AppendLine( " --- | --- | --- " );
                }
                else
                    sb.AppendLine( "-----" + item.Key + "------" );

                //For each procedure, add it to the text file
                foreach( var procedure in item )
                {
                    //Return type
                    sb.Append( procedure.Value.ReturnType.Name );
                    var genericReturnParameters = procedure.Value.ReturnType.GenericTypeArguments;
                    if( genericReturnParameters.Length > 0 )
                    {
                        sb.Append( "<" );

                        for( int j = 0; j < genericReturnParameters.Length; j++ )
                        {
                            sb.Append( genericReturnParameters[ j ].Name );
                            if( j + 1 < genericReturnParameters.Length )
                                sb.Append( ", " );

                            if( genericReturnParameters[ j ].GenericTypeArguments.Length > 0 )
                            {
                                var subGenericReturnParameters = genericReturnParameters[ j ].GenericTypeArguments;
                                sb.Append( "<" );

                                for( int k = 0; k < subGenericReturnParameters.Length; k++ )
                                {
                                    sb.Append( subGenericReturnParameters[ k ].Name );
                                    if( k + 1 < subGenericReturnParameters.Length )
                                        sb.Append( ", " );
                                }

                                sb.Append( ">" );
                            }
                        }

                        sb.Append( ">" );
                    }

                    if( githubWiki )
                        sb.Append( " | " );

                    //Procedure name
                    sb.Append( " " + procedure.Key );
                    var parameters = procedure.Value.GetParameters();

                    if( githubWiki )
                        sb.Append( " | " );
                    else
                        sb.Append( "(" + ( parameters.Length > 0 ? " " : "" ) );

                    //For each parameter, write the type (possibly including generic parameters) followed by the name and finally followed by the default value if it has one
                    for( int i = 0; i < parameters.Length; i++ )
                    {
                        //Parameter type name
                        sb.Append( parameters[ i ].ParameterType.Name );
                        //If it has generic types (I.E. lists), add those to the parameter type name too, nicely formatted
                        var genericParameters = parameters[ i ].ParameterType.GenericTypeArguments;
                        if( genericParameters.Length > 0 )
                        {
                            sb.Append( "<" );

                            for( int j = 0; j < genericParameters.Length; j++ )
                            {
                                sb.Append( genericParameters[ j ].Name );
                                if( j + 1 < genericParameters.Length )
                                    sb.Append( ", " );
                            }

                            sb.Append( ">" );
                        }
                        //Parameter name
                        sb.Append( " " + parameters[ i ].Name );
                        if( parameters[ i ].HasDefaultValue )
                        {
                            sb.Append( " = " + ( parameters[ i ].DefaultValue != null ? parameters[ i ].DefaultValue : "null" ) );
                        }
                        if( i + 1 < parameters.Length )
                            sb.Append( ", " );
                    }

                    if( !githubWiki )
                        sb.Append( ( parameters.Length > 0 ? " " : "" ) + ")" );

                    sb.AppendLine();
                }

                sb.AppendLine();
            }

            var result = sb.ToString();

            var splitResult = result.Split( new[] { Environment.NewLine }, StringSplitOptions.None );
            //Format spaces such that the procedure names all are at the same column
            if( formatSpaces )
            {
                var maxLength = 0;
                for( int i = 0; i < splitResult.Length; i++ )
                {
                    if( splitResult[ i ].Length >= 1 && splitResult[ i ].Substring( 0, 1 ) != "-" )
                    {
                        if( splitResult[ i ].IndexOf( ' ' ) > maxLength )
                            maxLength = splitResult[ i ].IndexOf( ' ' );
                    }
                }

                for( int i = 0; i < splitResult.Length; i++ )
                {
                    if( splitResult[ i ].Length >= 1 && splitResult[ i ].Substring( 0, 1 ) != "-" )
                    {
                        var firstSpaceIndex = splitResult[ i ].IndexOf( ' ' );

                        if( firstSpaceIndex < maxLength )
                        {
                            splitResult[ i ] = splitResult[ i ].Substring( 0, firstSpaceIndex ) + String.Join( "", Enumerable.Repeat( " ", maxLength - firstSpaceIndex ) ) + splitResult[ i ].Substring( firstSpaceIndex );
                        }
                    }
                }
            }

            //Remove all the irrelevant Task`1 and replace Task with void
            for( int i = 0; i < splitResult.Length; i++ )
            {
                if( splitResult[ i ].Length >= 6 && splitResult[ i ].Substring( 0, 6 ).Equals( "Task`1" ) )
                {
                    var lessThanIndex = splitResult[ i ].IndexOf( '<' );
                    var greaterThanIndex = splitResult[ i ].IndexOf( '>' );
                    splitResult[ i ] = splitResult[ i ].Remove( 0, lessThanIndex + 1 ).Remove( greaterThanIndex - lessThanIndex - 1, 1 );
                }
                else if( splitResult[ i ].Length >= 4 && splitResult[ i ].Substring( 0, 4 ).Equals( "Task" ) )
                    splitResult[ i ] = "void" + splitResult[ i ].Substring( 4 );
            }

            result = string.Join( Environment.NewLine, splitResult );

            result = result.Replace( "`1", "" );
            result = result.Replace( "`2", "" );
            result = result.Replace( "`3", "" );

            //Escape markdown characters
            if( githubWiki )
            {
                result = result.Replace( "`", "\\`" );
                result = result.Replace( "<", "\\<" );
                result = result.Replace( ">", "\\>" );
            }

#if DEBUG
            File.WriteAllText( Directory.GetCurrentDirectory() + "\\procedures.txt", result );
#endif

            return result;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sinceDate"></param>
        /// <returns></returns>
        [WampProcedure( "erp.debug.importttidata" )]
        public async Task<ImportTTIDataResult> DebugImportTTIData( DateTime? sinceDate = null )
        {
            int errorsOccured = 0, modifiedItems = 0, createdItems = 0;

            using( var conn = new SqlConnection( @"Data Source=94.189.39.182,49225;Initial Catalog=SDBOSSQL;User ID=testtest;Password=@magerT0rvAdmin" ) )
            {
                await conn.OpenAsync();

                var categoryRepo = await Database.GetAsync<Category>();
                var productRepo = await Database.GetAsync<Product>();
                var supplierRepo = await Database.GetAsync<Supplier>();
                var customerRepo = await Database.GetAsync<Customer>();
                var productGroupRepo = await Database.GetAsync<ProductGroup>();

                using( var command = new SqlCommand( "ExportDataToNewBoss", conn ) { CommandType = System.Data.CommandType.StoredProcedure } )
                {
                    command.Parameters.Add( "sinceDate", System.Data.SqlDbType.DateTime );
                    command.Parameters[ "sinceDate" ].Value = sinceDate;
                    using( var reader = await command.ExecuteReaderAsync() )
                    {


                        /*var categories = new Dictionary<string, Category>();
                        while( await reader.ReadAsync() )
                        {
                            try
                            {
                                var newCategory = new Category();
                                newCategory.Name = reader[ "kategori" ] as string;

                                newCategory.LegacyData = new Dictionary<string, object>();
                                newCategory.LegacyData.Add( "id", reader[ "id" ] );

                                categories.Add( reader[ "id" ].ToString(), newCategory );
                                await categoryRepo.InsertAsync( newCategory );
                            }
                            catch( Exception e )
                            {
                                log.LogError( Event.EXCEPTIONCAUGHT, e, $"ERROR! An error happened while attempting to import Category with id '{reader[ "id" ]}'" );
                            }

                        }

                        await reader.NextResultAsync();
                        while( await reader.ReadAsync() )
                        {
                            try
                            {
                                var newCategory = new Category();
                                newCategory.Name = reader[ "underkategori" ] as string;

                                Category existingCategory = null;
                                if( categories.TryGetValue( ( reader[ "id" ].ToString() ), out existingCategory ) )
                                    newCategory.ParentCategory = new DBRef( existingCategory.Id );

                                newCategory.LegacyData = new Dictionary<string, object>();
                                newCategory.LegacyData.Add( "id", reader[ "id" ] );
                                newCategory.LegacyData.Add( "kategoriid", reader[ "kategoriid" ] );

                                categories.Add( reader[ "id" ].ToString(), newCategory );
                                await categoryRepo.InsertAsync( newCategory );
                            }
                            catch( Exception e )
                            {
                                log.LogError( Event.EXCEPTIONCAUGHT, e, $"ERROR! An error happened while attempting to import Category with id '{reader[ "id" ]}'" );
                            }
                        }

                        await reader.NextResultAsync();*/
                        var createNew = false;

                        var productGroups = productGroupRepo.Query.Where( x => ( x.LegacyData[ "oldType" ] as int? ) == 0 ).ToDictionary( x => x.LegacyData[ "id" ].ToString() );
                        while( await reader.ReadAsync() )
                        {
                            try
                            {
                                ProductGroup newProductGroup;

                                if( createNew = !( productGroups.TryGetValue( reader[ "id" ].ToString(), out newProductGroup ) ) )
                                    newProductGroup = new ProductGroup();

                                newProductGroup.Name = reader[ "name" ] as string;

                                newProductGroup.LegacyData = new Dictionary<string, object>();
                                newProductGroup.LegacyData[ "id" ] = reader[ "id" ];
                                newProductGroup.LegacyData[ "oldType" ] = 0;

                                if( createNew )
                                {
                                    await productGroupRepo.InsertAsync( newProductGroup );
                                    createdItems++;
                                }
                                else
                                {
                                    await productGroupRepo.ReplaceAsync( newProductGroup );
                                    modifiedItems++;
                                }

                                productGroups[ reader[ "id" ].ToString() ] = newProductGroup;
                            }
                            catch( Exception e )
                            {
                                log.LogError( Event.EXCEPTIONCAUGHT, e, $"ERROR! An error happened while attempting to import ProductGroup with id '{reader[ "id" ]}'" );
                                errorsOccured++;
                            }
                        }

                        await reader.NextResultAsync();
                        var productGroups1 = productGroupRepo.Query.Where( x => ( x.LegacyData[ "oldType" ] as int? ) == 1 ).ToDictionary( x => x.LegacyData[ "id" ].ToString() );
                        while( await reader.ReadAsync() )
                        {
                            try
                            {
                                ProductGroup newProductGroup;

                                if( createNew = !( productGroups1.TryGetValue( reader[ "id" ].ToString(), out newProductGroup ) ) )
                                    newProductGroup = new ProductGroup();

                                newProductGroup.Name = reader[ "name" ] as string;

                                newProductGroup.LegacyData = new Dictionary<string, object>();
                                newProductGroup.LegacyData[ "id" ] = reader[ "id" ];
                                newProductGroup.LegacyData[ "oldType" ] = 1;

                                if( createNew )
                                {
                                    await productGroupRepo.InsertAsync( newProductGroup );
                                    createdItems++;
                                }
                                else
                                {
                                    await productGroupRepo.ReplaceAsync( newProductGroup );
                                    modifiedItems++;
                                }

                                productGroups1[ reader[ "id" ].ToString() ] = newProductGroup;
                            }
                            catch( Exception e )
                            {
                                log.LogError( Event.EXCEPTIONCAUGHT, e, $"ERROR! An error happened while attempting to import ProductGroup1 with id '{reader[ "id" ]}'" );
                                errorsOccured++;
                            }
                        }

                        await reader.NextResultAsync();
                        var productGroups2 = productGroupRepo.Query.Where( x => ( x.LegacyData[ "oldType" ] as int? ) == 2 ).ToDictionary( x => x.LegacyData[ "id" ].ToString() );
                        while( await reader.ReadAsync() )
                        {
                            try
                            {
                                ProductGroup newProductGroup;

                                if( createNew = !( productGroups2.TryGetValue( reader[ "id" ].ToString(), out newProductGroup ) ) )
                                    newProductGroup = new ProductGroup();

                                newProductGroup.Name = "GDS - " + ( reader[ "name" ] as string );

                                newProductGroup.LegacyData = new Dictionary<string, object>();
                                newProductGroup.LegacyData[ "id" ] = reader[ "id" ];
                                newProductGroup.LegacyData[ "oldType" ] = 2;

                                if( createNew )
                                {
                                    await productGroupRepo.InsertAsync( newProductGroup );
                                    createdItems++;
                                }
                                else
                                {
                                    await productGroupRepo.ReplaceAsync( newProductGroup );
                                    modifiedItems++;
                                }

                                productGroups2[ reader[ "id" ].ToString() ] = newProductGroup;
                            }
                            catch( Exception e )
                            {
                                log.LogError( Event.EXCEPTIONCAUGHT, e, $"ERROR! An error happened while attempting to import ProductGroup2 with id '{reader[ "id" ]}'" );
                                errorsOccured++;
                            }
                        }

                        await reader.NextResultAsync();
                        var productGroups3 = productGroupRepo.Query.Where( x => ( x.LegacyData[ "oldType" ] as int? ) == 3 ).ToDictionary( x => x.LegacyData[ "id" ].ToString() );
                        while( await reader.ReadAsync() )
                        {
                            try
                            {
                                ProductGroup newProductGroup;

                                if( createNew = !( productGroups3.TryGetValue( reader[ "id" ].ToString(), out newProductGroup ) ) )
                                    newProductGroup = new ProductGroup();

                                newProductGroup.Name = reader[ "name" ] as string;

                                newProductGroup.LegacyData = new Dictionary<string, object>();
                                newProductGroup.LegacyData[ "id" ] = reader[ "id" ];
                                newProductGroup.LegacyData[ "oldType" ] = 3;

                                if( createNew )
                                {
                                    await productGroupRepo.InsertAsync( newProductGroup );
                                    createdItems++;
                                }
                                else
                                {
                                    await productGroupRepo.ReplaceAsync( newProductGroup );
                                    modifiedItems++;
                                }

                                productGroups3[ reader[ "id" ].ToString() ] = newProductGroup;
                            }
                            catch( Exception e )
                            {
                                log.LogError( Event.EXCEPTIONCAUGHT, e, $"ERROR! An error happened while attempting to import ProductGroup3 with id '{reader[ "id" ]}'" );
                                errorsOccured++;
                            }
                        }


                        //Suppliers and Customers
                        await reader.NextResultAsync();
                        var suppliers = supplierRepo.Query.Where( x => x.LegacyData[ "ad_Account" ] != null ).ToDictionary( x => x.LegacyData[ "ad_Account" ].ToString() );
                        var customers = customerRepo.Query.Where( x => x.LegacyData[ "ad_Account" ] != null ).ToDictionary( x => x.LegacyData[ "ad_Account" ].ToString() );
                        while( await reader.ReadAsync() )
                        {
                            try
                            {
                                //Kunde
                                if( ( reader[ "addrType" ].ToString() ).Equals( "2" ) )
                                {
                                    Customer newCustomer;
                                    if( createNew = !( customers.TryGetValue( reader[ "ad_Account" ].ToString(), out newCustomer ) ) )
                                        newCustomer = new Customer();

                                    newCustomer.CompanyName = reader[ "CompanyName" ] as string;
                                    newCustomer.Department = reader[ "Department" ] as string;

                                    newCustomer.Address = new Address();
                                    newCustomer.Address.RoadName = reader[ "Address" ] as string;
                                    newCustomer.Address.CareOf = reader[ "Address2" ] as string;
                                    newCustomer.Address.ZipCode = reader[ "PostalCode" ] as string;
                                    newCustomer.Address.CityName = reader[ "City" ] as string;

                                    newCustomer.PhoneNumber = reader[ "Phone" ] as string;
                                    newCustomer.Email = reader[ "Email" ] as string;
                                    newCustomer.ContactName = reader[ "ContactPersonFinance" ] as string;
                                    newCustomer.Deleted = ( reader[ "Inactive" ] as bool? ) ?? false;

                                    newCustomer.CreatedDateTime = ( reader[ "DateCreate" ] as DateTime? ) ?? DateTime.Now;
                                    newCustomer.ModifiedDateTime = ( reader[ "DateUpdate" ] as DateTime? );

                                    newCustomer.EAN = reader[ "eancode" ] as string;

                                    newCustomer.LegacyData = new Dictionary<string, object>();
                                    newCustomer.LegacyData.Add( "addressID", reader[ "addressID" ] );
                                    newCustomer.LegacyData.Add( "ad_Account", reader[ "ad_Account" ] );

                                    if( createNew )
                                    {
                                        await customerRepo.InsertAsync( newCustomer );
                                        createdItems++;
                                    }
                                    else
                                    {
                                        await customerRepo.ReplaceAsync( newCustomer );
                                        modifiedItems++;
                                    }

                                    customers[ reader[ "ad_Account" ].ToString() ] = newCustomer;
                                }
                                //Leverandør
                                else if( ( reader[ "addrType" ].ToString() ).Equals( "3" ) )
                                {
                                    Supplier newSupplier;
                                    if( createNew = !( suppliers.TryGetValue( reader[ "ad_Account" ].ToString(), out newSupplier ) ) )
                                        newSupplier = new Supplier();

                                    newSupplier.CompanyName = reader[ "CompanyName" ] as string;

                                    newSupplier.Address = new Address();
                                    newSupplier.Address.RoadName = reader[ "Address" ] as string;
                                    newSupplier.Address.CareOf = reader[ "Address2" ] as string;
                                    newSupplier.Address.ZipCode = reader[ "PostalCode" ] as string;
                                    newSupplier.Address.CityName = reader[ "City" ] as string;

                                    newSupplier.PhoneNumber = reader[ "Phone" ] as string;
                                    newSupplier.Email = reader[ "Email" ] as string;
                                    newSupplier.ContactName = reader[ "ContactPersonFinance" ] as string;
                                    newSupplier.Deleted = ( reader[ "Inactive" ] as bool? ) ?? false;

                                    newSupplier.CreatedDateTime = ( reader[ "DateCreate" ] as DateTime? ) ?? DateTime.Now;
                                    newSupplier.ModifiedDateTime = ( reader[ "DateUpdate" ] as DateTime? );

                                    var regex = Regex.Match( ( reader[ "Delivery" ] as string ) ?? "", @"[\d]+" );
                                    newSupplier.FreeDeliveryPrice = regex.Success ? float.Parse( regex.Value ) : 0f;

                                    newSupplier.LegacyData = new Dictionary<string, object>();
                                    newSupplier.LegacyData.Add( "addressID", reader[ "addressID" ] );
                                    newSupplier.LegacyData.Add( "ad_Account", reader[ "ad_Account" ] );

                                    if( createNew )
                                    {
                                        await supplierRepo.InsertAsync( newSupplier );
                                        createdItems++;
                                    }
                                    else
                                    {
                                        await supplierRepo.ReplaceAsync( newSupplier );
                                        modifiedItems++;
                                    }

                                    suppliers[ reader[ "ad_Account" ].ToString() ] = newSupplier;
                                }
                            }
                            catch( Exception e )
                            {
                                log.LogError( Event.EXCEPTIONCAUGHT, e, $"ERROR! An error happened while attempting to import Address with id '{reader[ "addressID" ]}'" );
                                errorsOccured++;
                            }
                        }

                        //Products
                        await reader.NextResultAsync();
                        var products = productRepo.Query.Where( x => x.LegacyData[ "prodid" ] != null ).ToDictionary( x => x.LegacyData[ "prodid" ] as string );
                        while( await reader.ReadAsync() )
                        {
                            try
                            {
                                if( string.IsNullOrEmpty( reader[ "prodid" ] as string ) )
                                    continue;

                                Product newProduct;
                                if( createNew = !( products.TryGetValue( reader[ "prodid" ] as string, out newProduct ) ) )
                                    newProduct = new Product();

                                if( ( reader[ "description" ] ?? "" ).ToString().Equals( ( reader[ "Hyldetekst" ] ?? "" ).ToString(), StringComparison.OrdinalIgnoreCase ) && ( reader[ "description" ] ?? "" ).ToString().Equals( ( reader[ "Bontekst" ] ?? "" ).ToString(), StringComparison.OrdinalIgnoreCase ) )
                                {
                                    newProduct.TitlePOS = reader[ "description" ] as string;
                                    newProduct.ReuseTitlePOSAsReceiptText = true;
                                    newProduct.ReuseTitlePOSAsShelfText = true;
                                }
                                else
                                {
                                    newProduct.TitlePOS = reader[ "description" ] as string;
                                    newProduct.ShelfText = reader[ "Hyldetekst" ] as string;
                                    newProduct.ReceiptText = reader[ "Bontekst" ] as string;
                                    newProduct.ReuseTitlePOSAsReceiptText = false;
                                    newProduct.ReuseTitlePOSAsShelfText = false;
                                }

                                newProduct.Deleted = ( reader[ "inactive" ] as bool? ) ?? false;
                                newProduct.EAN = reader[ "eancode" ] as string;

                                newProduct.PurchasePrice = ( reader[ "pricecost" ] as decimal? ).HasValue ? Convert.ToSingle( reader[ "pricecost" ] ) as float? : null;

                                newProduct.SupplierProductId = reader[ "vendcode" ] as string;

                                var vendid = reader[ "vendid" ].ToString();
                                if( !string.IsNullOrEmpty( vendid ) )
                                {
                                    Supplier existingSupplier = null;
                                    newProduct.Supplier = suppliers.TryGetValue( vendid, out existingSupplier ) ? new DBRef( existingSupplier.Id ) : null;
                                }

                                newProduct.VATMultiplier = ( reader[ "Momssats" ] as decimal? ).HasValue ? ( ( Convert.ToSingle( reader[ "Momssats" ] ) as float? ) / 100f ) + 1f : null;
                                newProduct.SalesPrice = ( reader[ "Pris" ] as decimal? ).HasValue ? Convert.ToSingle( reader[ "Pris" ] ) as float? : null;
                                newProduct.RetailPrice = ( reader[ "VejlPris" ] as decimal? ).HasValue ? Convert.ToSingle( reader[ "VejlPris" ] ) as float? : null;
                                if( ( reader[ "vareart" ] as int? ).HasValue )
                                    newProduct.Variant = ( reader[ "vareart" ] as int? ).Value.ToString();

                                newProduct.CreatedDateTime = ( reader[ "DateCreate" ] as DateTime? ) ?? DateTime.Now;
                                newProduct.ModifiedDateTime = ( reader[ "DateChange" ] as DateTime? );

                                var productGroupList = new List<DBRef>();
                                var varegrp = reader[ "varegrp" ].ToString();
                                if( !string.IsNullOrEmpty( varegrp ) )
                                {
                                    ProductGroup existingProductGroup = null;
                                    if( productGroups.TryGetValue( varegrp, out existingProductGroup ) )
                                        productGroupList.Add( new DBRef( existingProductGroup.Id ) );
                                }

                                var sortiment1 = reader[ "sortiment1" ].ToString();
                                if( !string.IsNullOrEmpty( sortiment1 ) )
                                {
                                    ProductGroup existingProductGroup = null;
                                    if( productGroups1.TryGetValue( sortiment1, out existingProductGroup ) )
                                        productGroupList.Add( new DBRef( existingProductGroup.Id ) );
                                }
                                var sortiment2 = reader[ "sortiment2" ].ToString();
                                if( !string.IsNullOrEmpty( sortiment2 ) )
                                {
                                    ProductGroup existingProductGroup = null;
                                    if( productGroups2.TryGetValue( sortiment2, out existingProductGroup ) )
                                        productGroupList.Add( new DBRef( existingProductGroup.Id ) );
                                }
                                var sortiment3 = reader[ "sortiment3" ].ToString();
                                if( !string.IsNullOrEmpty( sortiment3 ) )
                                {
                                    ProductGroup existingProductGroup = null;
                                    if( productGroups3.TryGetValue( sortiment3, out existingProductGroup ) )
                                        productGroupList.Add( new DBRef( existingProductGroup.Id ) );
                                }

                                newProduct.ProductGroup = productGroupList.ToArray();

                                //var underkategoriid = reader[ "underkategoriid" ].ToString();
                                //if( !string.IsNullOrEmpty( underkategoriid ) )
                                //{
                                //    Category existingCategory = null;
                                //    if( categories.TryGetValue( underkategoriid, out existingCategory ) )
                                //        newProduct.Category = newProduct.Category.Concat( new[] { new DBRef( existingCategory.Id ) } ).ToArray();
                                //}

                                newProduct.TitleWeb = reader[ "titel" ] as string;
                                newProduct.DescriptionWeb = reader[ "beskrivelse" ] as string;
                                newProduct.StorePickup = ( reader[ "afh_i_butik" ] as bool? ) ?? false;
                                newProduct.WebshopPickup = ( reader[ "saelges_ikke_i_shop" ] as bool? ) ?? false;
                                newProduct.ParcelQuantity = ( reader[ "colli" ] as decimal? ).HasValue ? Convert.ToInt32( reader[ "colli" ] ) as int? : null;

                                newProduct.LegacyData = new Dictionary<string, object>();
                                newProduct.LegacyData.Add( "id", reader[ "id" ] );
                                newProduct.LegacyData.Add( "prodid", reader[ "prodid" ] );

                                if( reader[ "kg" ] != null && reader[ "kg" ] != DBNull.Value )
                                    newProduct.LegacyData.Add( "kg", reader[ "kg" ] );

                                if( createNew )
                                {
                                    await productRepo.InsertAsync( newProduct );
                                    createdItems++;
                                }
                                else
                                {
                                    await productRepo.ReplaceAsync( newProduct );
                                    modifiedItems++;
                                }

                                products[ reader[ "prodid" ] as string ] = newProduct;
                            }
                            catch( Exception e )
                            {
                                log.LogError( Event.EXCEPTIONCAUGHT, e, $"ERROR! An error happened while attempting to import Product with id '{reader[ "id" ]}'" );
                                errorsOccured++;
                            }
                        }

                        /*var products = productRepo.Query.ToList();
                        await reader.NextResultAsync();
                        await reader.NextResultAsync();
                        await reader.NextResultAsync();
                        await reader.NextResultAsync();
                        await reader.NextResultAsync();*/


                        //Discounts
                        await reader.NextResultAsync();
                        var discountRepo = await Database.GetAsync<DiscountBase>();
                        var discounts = discountRepo.Query.Where( x => x.LegacyData[ "ident" ] != null ).ToDictionary( x => ( x.LegacyData[ "ident" ] as int? ).ToString() );
                        while( await reader.ReadAsync() )
                        {
                            try
                            {
                                var discountType = reader[ "type" ] as int?;
                                if( discountType == null || !discountType.HasValue )
                                    continue;

                                DiscountBase newDiscount = null;
                                createNew = !( discounts.TryGetValue( ( reader[ "ident" ] as int? ).ToString(), out newDiscount ) );

                                switch( discountType.Value )
                                {
                                    case 1:
                                        {
                                            QuantityDiscount unpackedDiscount = createNew ? new QuantityDiscount() : (QuantityDiscount) newDiscount;
                                            unpackedDiscount.DiscountType = DiscountType.Quantity;
                                            unpackedDiscount.MinimumQuantity = ( reader[ "antal" ] as decimal? ).HasValue ? Convert.ToInt32( reader[ "antal" ] ) : 1;
                                            unpackedDiscount.Price = ( reader[ "gruppepris" ] as decimal? ).HasValue ? Convert.ToSingle( reader[ "gruppepris" ] ) : 0f;
                                            newDiscount = unpackedDiscount;
                                            break;
                                        }
                                    case 2:
                                        {
                                            PackageDiscount unpackedDiscount = createNew ? new PackageDiscount() : (PackageDiscount) newDiscount;
                                            unpackedDiscount.DiscountType = DiscountType.Package;
                                            unpackedDiscount.Price = ( reader[ "gruppepris" ] as decimal? ).HasValue ? Convert.ToSingle( reader[ "gruppepris" ] ) : 0f;
                                            newDiscount = unpackedDiscount;
                                            break;
                                        }
                                    default:
                                        continue;
                                }

                                newDiscount.Name = reader[ "rabatgruppe" ] as string;
                                newDiscount.FromDateTime = ( reader[ "startdato" ] as DateTime? ) ?? DateTime.Now;
                                newDiscount.ToDateTime = ( reader[ "slutdato" ] as DateTime? );
                                newDiscount.CreatedDateTime = ( reader[ "timestamp" ] as DateTime? ) ?? DateTime.Now;

                                newDiscount.LegacyData = new Dictionary<string, object>();
                                newDiscount.LegacyData.Add( "ident", reader[ "ident" ] as int? );

                                discounts[ ( reader[ "ident" ] as int? ).ToString() ] = newDiscount;

                                if( createNew )
                                    createdItems++;
                                else
                                    modifiedItems++;
                            }
                            catch( Exception e )
                            {
                                log.LogError( Event.EXCEPTIONCAUGHT, e, $"ERROR! An error happened while attempting to import Discount with id '{reader[ "ident" ]}'" );
                                errorsOccured++;
                            }
                        }

                        //DiscountItems
                        await reader.NextResultAsync();

                        var discountItems = new Dictionary<int, List<PackageDiscountItem>>();
                        foreach( var discount in discounts )
                        {
                            switch( discount.Value.DiscountType )
                            {
                                case DiscountType.Package:
                                    {
                                        if( ( (PackageDiscount) discount.Value ).Products == null )
                                            continue;
                                        discountItems[ discount.Value.LegacyData[ "ident" ] as int? ?? 0 ] = ( (PackageDiscount) discount.Value ).Products;
                                    }
                                    break;
                                case DiscountType.Quantity:
                                    {
                                        if( ( (QuantityDiscount) discount.Value ).Products == null )
                                            continue;
                                        discountItems[ discount.Value.LegacyData[ "ident" ] as int? ?? 0 ] = ( (QuantityDiscount) discount.Value ).Products.Select( x => new PackageDiscountItem() { Product = x } ).ToList();
                                    }
                                    break;
                                default:
                                    break;
                            }
                        }

                        while( await reader.ReadAsync() )
                        {
                            try
                            {
                                PackageDiscountItem newItem = null;
                                List<PackageDiscountItem> items = null;
                                if( createNew = !( discountItems.TryGetValue( ( reader[ "rabatgruppe_ident" ] as int? ) ?? 0, out items ) && ( newItem = ( items?.FirstOrDefault( x => ( x.LegacyData[ "rabatgruppe_ident" ] as int? ) == reader[ "rabatgruppe_ident" ] as int? ) ) ) != null ) )
                                    newItem = new PackageDiscountItem();

                                var rabId = ( reader[ "rabatgruppe_ident" ] as int? ).ToString();
                                DiscountBase discount = null;
                                if( string.IsNullOrEmpty( rabId ) || !discounts.TryGetValue( rabId, out discount ) )
                                    continue;

                                switch( discount.DiscountType )
                                {
                                    case DiscountType.Package:
                                        newItem.Quantity = ( reader[ "antal" ] as decimal? ).HasValue ? Convert.ToInt32( reader[ "antal" ] ) : 1;
                                        break;
                                    case DiscountType.Quantity:
                                        newItem.Quantity = 1;
                                        break;
                                    default:
                                        continue;
                                }

                                var prodid = reader[ "prodid" ] as string;
                                if( string.IsNullOrEmpty( prodid ) )
                                    continue;

                                Product existingProduct;

                                if( !products.TryGetValue( prodid, out existingProduct ) || existingProduct == null )
                                    continue;

                                newItem.Product = new DBRef( existingProduct.Id );

                                newItem.LegacyData = new Dictionary<string, object>();
                                newItem.LegacyData.Add( "ident", reader[ "ident" ] as string );
                                newItem.LegacyData.Add( "rabatgruppe_ident", reader[ "rabatgruppe_ident" ] as int? );
                                newItem.LegacyData.Add( "prodid", prodid );
                                newItem.LegacyData.Add( "sb_produkter_id", reader[ "sb_produkter_id" ] as int? );

                                List<PackageDiscountItem> list = null;
                                if( discountItems.TryGetValue( ( reader[ "rabatgruppe_ident" ] as int? ) ?? 0, out list ) )
                                {
                                    if( createNew )
                                        list.Add( newItem );
                                    else
                                    {
                                        list.RemoveAll( x => x.Product.Id == newItem.Product.Id );
                                        list.Add( newItem );
                                    }
                                    discountItems[ ( reader[ "rabatgruppe_ident" ] as int? ) ?? 0 ] = list;
                                }
                                else
                                {
                                    list = new List<PackageDiscountItem>();
                                    list.Add( newItem );
                                    discountItems.Add( ( reader[ "rabatgruppe_ident" ] as int? ) ?? 0, list );
                                }

                                if( createNew )
                                    createdItems++;
                                else
                                    modifiedItems++;
                            }
                            catch( Exception e )
                            {
                                log.LogError( Event.EXCEPTIONCAUGHT, e, $"ERROR! An error happened while attempting to import DiscountItem with id '{reader[ "ident" ]}'" );
                                errorsOccured++;
                            }
                        }

                        foreach( var kvp in discounts )
                        {
                            switch( kvp.Value.DiscountType )
                            {
                                case DiscountType.Package:
                                    {
                                        var newDiscount = kvp.Value as PackageDiscount;
                                        var ident = newDiscount.LegacyData[ "ident" ] as int?;
                                        if( !ident.HasValue )
                                            continue;
                                        List<PackageDiscountItem> items = null;
                                        if( !discountItems.TryGetValue( ident.Value, out items ) )
                                            continue;
                                        newDiscount.Products = items;
                                        //if( ((IMongoQueryable<DiscountBase>) discountRepo.Query).FirstOrDefaultAsync( x => (x.LegacyData["ident"] as int?) == ident) != null )
                                        if( string.IsNullOrEmpty( newDiscount.Id ) )
                                        {
                                            await discountRepo.InsertAsync( newDiscount );
                                        }
                                        else
                                        {
                                            await discountRepo.ReplaceAsync( newDiscount );
                                        }
                                    }
                                    break;
                                case DiscountType.Quantity:
                                    {
                                        var newDiscount = kvp.Value as QuantityDiscount;
                                        var ident = newDiscount.LegacyData[ "ident" ] as int?;
                                        if( !ident.HasValue )
                                            continue;
                                        List<PackageDiscountItem> items = null;
                                        if( !discountItems.TryGetValue( ident.Value, out items ) )
                                            continue;

                                        newDiscount.Products = items.Select( x => x.Product ).ToList();
                                        if( string.IsNullOrEmpty( newDiscount.Id ) )
                                        {
                                            await discountRepo.InsertAsync( newDiscount );
                                        }
                                        else
                                        {
                                            await discountRepo.ReplaceAsync( newDiscount );
                                        }
                                    }
                                    break;
                            }
                        }

                        //Campaigns and PriceSchedules
                        await reader.NextResultAsync();
                        var campaignRepo = Database.Get<Campaign>();
                        var campaigns = campaignRepo.Query.Where( x => x.Number != null && x.Number != "" ).ToDictionary( x => x.Number );
                        while( await reader.ReadAsync() )
                        {
                            try
                            {
                                var campaignNumber = reader[ "kampagnenr" ] as string;
                                if( string.IsNullOrEmpty( campaignNumber ) )
                                    continue;

                                Campaign newCampaign = null;
                                if( createNew = !( campaigns.TryGetValue( campaignNumber, out newCampaign ) ) )
                                    newCampaign = new Campaign() { DiscountType = DiscountType.Campaign };

                                newCampaign.Number = campaignNumber;
                                newCampaign.StartDateTime = ( reader[ "startdato" ] as DateTime? ) ?? DateTime.Now;
                                newCampaign.EndDateTime = ( reader[ "slutdato" ] as DateTime? ) ?? null;
                                newCampaign.Name = reader[ "kampagnenavn" ] as string;
                                newCampaign.CreatedDateTime = ( reader[ "startdato" ] as DateTime? ) ?? DateTime.Now;

                                newCampaign.Products = new List<CampaignItem>();
                                campaigns[ newCampaign.Number ] = newCampaign;

                                if( createNew )
                                    createdItems++;
                                else
                                    modifiedItems++;
                            }
                            catch( Exception e )
                            {
                                log.LogError( Event.EXCEPTIONCAUGHT, e, $"ERROR! An error happened while attempting to import Campaign with campaign number '{reader[ "kampagnenr" ]}'" );
                                errorsOccured++;
                            }
                        }

                        //Campaign items
                        await reader.NextResultAsync();
                        var priceScheduleRepo = Database.Get<PriceSchedule>();
                        var priceSchedules = priceScheduleRepo.Query.Where( x => x.FromDateTime != DateTime.MinValue ).ToDictionary( x => x.FromDateTime );
                        while( await reader.ReadAsync() )
                        {
                            try
                            {
                                Campaign campaign = null;
                                var campaignNumber = reader[ "kampagnenr" ] as string;
                                if( string.IsNullOrEmpty( campaignNumber ) || !campaigns.TryGetValue( campaignNumber, out campaign ) )
                                    continue;

                                var startDate = reader[ "startdato" ] as DateTime?;
                                if( startDate == null )
                                    continue;

                                var price = reader[ "kampagnepris" ] as decimal?;
                                if( !price.HasValue )
                                    continue;

                                var returnPrice = reader[ "returpris" ] as decimal?;
                                if( !returnPrice.HasValue )
                                    continue;

                                var prodid = reader[ "prodid" ] as string;
                                if( string.IsNullOrEmpty( prodid ) )
                                    continue;

                                Product existingProduct;
                                products.TryGetValue( prodid, out existingProduct );
                                if( existingProduct == null )
                                    continue;

                                CampaignItem newItem = null;
                                if( createNew = !( ( newItem = campaign.Products.FirstOrDefault( x => x.Product.Id == existingProduct.Id ) ) != null ) )
                                    newItem = new CampaignItem();

                                newItem.CampaignPrice = Convert.ToSingle( reader[ "kampagnepris" ] );

                                if( ( ( reader[ "status" ] as int? ) ?? 2 ) == 1 )
                                {
                                    PriceSchedule priceSchedule = null;
                                    PriceScheduleItem item = new PriceScheduleItem() { Product = new DBRef( existingProduct.Id ), NewPrice = Convert.ToSingle( returnPrice ) };
                                    if( priceSchedules.TryGetValue( startDate.Value, out priceSchedule ) )
                                    {
                                        priceSchedule.Items.RemoveAll( x => x.Product.Id == existingProduct.Id );
                                        priceSchedule.Items.Add( item );
                                    }
                                    else
                                    {
                                        priceSchedule = new PriceSchedule();
                                        priceSchedule.Items = new List<PriceScheduleItem>();
                                        priceSchedule.Items.Add( item );
                                        priceSchedule.FromDateTime = startDate.Value;
                                        priceSchedule.Executed = false;
                                        priceSchedule.Campaign = new DBRef( campaign.Id );
                                        priceSchedule.Name = "Prisændringer for kampagne nummer " + campaign.Number;
                                        priceSchedules.Add( startDate.Value, priceSchedule );
                                        createdItems++;
                                    }
                                }

                                newItem.Product = new DBRef( existingProduct.Id );
                                var avisSide = reader[ "avisside" ].ToString();
                                if( !string.IsNullOrWhiteSpace( avisSide ) )
                                    newItem.CataloguePage = avisSide;

                                newItem.CreatedDateTime = ( reader[ "timestamp" ] as DateTime? ) ?? DateTime.Now;

                                newItem.LegacyData = new Dictionary<string, object>();
                                newItem.LegacyData.Add( "id_karaka", ( reader[ "id_karaka" ] as int? ).Value );

                                if( !createNew )
                                    campaign.Products.RemoveAll( x => x.Product.Id == existingProduct.Id );
                                campaign.Products.Add( newItem );

                                if( createNew )
                                    createdItems++;
                                else
                                    modifiedItems++;
                            }
                            catch( Exception e )
                            {
                                log.LogError( Event.EXCEPTIONCAUGHT, e, $"ERROR! An error happened while attempting to import CampaignItem with id_karaka '{reader[ "id_karaka" ]}'" );
                                errorsOccured++;
                            }
                        }

                        foreach( var campaign in campaigns )
                        {
                            if( string.IsNullOrEmpty( campaign.Value.Id ) )
                            {
                                await campaignRepo.InsertAsync( campaign.Value );
                            }
                            else
                            {
                                await campaignRepo.ReplaceAsync( campaign.Value );
                            }
                        }

                        foreach( var priceSchedule in priceSchedules )
                        {
                            if( string.IsNullOrEmpty( priceSchedule.Value.Id ) )
                            {
                                await priceScheduleRepo.InsertAsync( priceSchedule.Value );
                            }
                            else
                            {
                                await priceScheduleRepo.ReplaceAsync( priceSchedule.Value );
                            }
                        }

                        if( errorsOccured > 0 )
                            Console.WriteLine( $"ERROR! While attempting to import data from the old BOSS, {errorsOccured + ( errorsOccured > 1 ? " errors" : " error" ) } occured. See the log for more details" );
                    }
                }
            }

            return new ImportTTIDataResult() { CreatedItems = createdItems, ModifiedItems = modifiedItems, ErrorsOccured = errorsOccured };
        }

        /*[WampProcedure( "erp.debug.exporttticampaigns" )]
        public async Task<int> DebugExportTTICampaigns()
        {
            using( var conn = new SqlConnection( @"Data Source=94.189.39.182,49225;Initial Catalog=SDBOSSQL;User ID=testtest;Password=@magerT0rvAdmin" ) )
            {
                await conn.OpenAsync();

                var campaignRepo = await Database.GetAsync<Campaign>();
                var campaigns = campaignRepo.Query.ToArray();

                var command = new SqlCommand( "DELETE FROM sb_kampagne", conn );
                await command.ExecuteNonQueryAsync();

                var bulkInsert = new SqlBulkCopy( conn, SqlBulkCopyOptions.KeepIdentity, null );
                bulkInsert.DestinationTableName = "sb_kampagne";
                bulkInsert.ColumnMappings.Add( "Number", "kampagnenr" );
                bulkInsert.ColumnMappings.Add( "Name", "kampagnenavn" );
                bulkInsert.ColumnMappings.Add( "StartDateTime", "startdato" );
                bulkInsert.ColumnMappings.Add( "EndDateTime", "slutdato" );

                using( var dataReader = new ObjectDataReader<Campaign>( campaigns ) )
                {
                    await bulkInsert.WriteToServerAsync( dataReader );
                }

                return campaigns.Length;
            }
        }*/
    }

    #endregion
}